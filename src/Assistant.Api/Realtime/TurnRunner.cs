using System.Diagnostics.CodeAnalysis;
using Assistant.Abstractions;
using Assistant.Abstractions.Channels;
using Assistant.Api.Channels;
using Assistant.Api.Speech;
using Assistant.Core.Conversations;
using Assistant.Extensions.Proactive;
using Microsoft.AspNetCore.SignalR;
using Serilog.Context;
using ChannelTurnOrigin = Assistant.Abstractions.Channels.TurnOrigin;

namespace Assistant.Api.Realtime;

[SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Detached turn boundary: no caller can observe failures, so they must be logged here.")]
internal sealed class TurnRunner(
    IServiceScopeFactory scopes,
    IHubContext<AssistantHub> hub,
    TurnConnections connections,
    VoiceTurnPipeline voice,
    TimeProvider clock,
    ILogger<TurnRunner> log) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<Task> _running = [];
    private readonly CancellationTokenSource _shutdown = new();

    public void Start(TurnRequest request, string connectionId)
        => _ = TrackAsync(() => RunAsync(request, HubOrigin(), CreateHubSink(connectionId)));

    public void Resume(TurnRequest request, string connectionId)
        => _ = TrackAsync(() => RunAsync(request, HubOrigin(), CreateHubSink(connectionId)));

    /// <summary>
    /// Same as <see cref="Resume"/> but awaitable so the expiry worker can finish a batch
    /// before listing again (and so drain waits on those tasks).
    /// </summary>
    public Task ResumeAsync(TurnRequest request, string connectionId)
        => TrackAsync(() => RunAsync(request, HubOrigin(), CreateHubSink(connectionId)));

    /// <summary>
    /// Runs a turn through <paramref name="sink"/> instead of a SignalR connection.
    /// The returned task is tracked for drain, same as <see cref="Start(TurnRequest, string)"/>.
    /// </summary>
    public Task Start(TurnRequest request, ChannelTurnOrigin origin, IInternalTurnSink sink)
        => TrackAsync(() => RunAsync(request, origin, sink));

    /// <summary>
    /// Awaitable resume used by confirmation expiry when the conversation belongs to a channel peer.
    /// </summary>
    public Task ResumeAsync(TurnRequest request, ChannelTurnOrigin origin, IInternalTurnSink sink)
        => TrackAsync(() => RunAsync(request, origin, sink));

    private HubTurnSink CreateHubSink(string connectionId)
        => new(hub, connections, voice, clock, log, connectionId);

    private static ChannelTurnOrigin HubOrigin() => new(ChannelId.Hub, null);

    private Task TrackAsync(Func<Task> work)
    {
        Task? task = null;
        task = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            finally
            {
                lock (_gate)
                {
                    _running.Remove(task!);
                }
            }
        });

        lock (_gate)
        {
            _running.Add(task);
        }

        if (task.IsCompleted)
        {
            lock (_gate)
            {
                _running.Remove(task);
            }
        }

        return task;
    }

    public async Task DrainAsync(TimeSpan gracePeriod, CancellationToken ct)
    {
        try
        {
            using var grace = new CancellationTokenSource(gracePeriod);
            await Task.WhenAll(Snapshot()).WaitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
            await _shutdown.CancelAsync();
            await Task.WhenAll(Snapshot()).WaitAsync(ct);
        }
    }

    private async Task RunAsync(TurnRequest request, ChannelTurnOrigin origin, IInternalTurnSink sink)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITurnOriginAccessor>().Set(origin);
        var orchestrator = scope.ServiceProvider.GetRequiredService<IConversationOrchestrator>();

        IDisposable? correlation = null;

        try
        {
            if (!request.IsResume
                && await TryBriefingShortcutAsync(scope.ServiceProvider, request, sink))
            {
                return;
            }

            await foreach (var update in orchestrator.RunTurnAsync(request, _shutdown.Token))
            {
                if (update is TurnAccepted accepted)
                {
                    correlation = LogContext.PushProperty("correlationId", accepted.TurnId);
                }

                await sink.HandleAsync(update, request, _shutdown.Token);
            }
        }
        catch (Exception e)
        {
            ApiLog.TurnRunnerFailed(log, e, request.ConversationId);
        }
        finally
        {
            try
            {
                await sink.CompleteAsync(_shutdown.Token);
            }
            finally
            {
                correlation?.Dispose();
            }
        }
    }

    private async Task<bool> TryBriefingShortcutAsync(
        IServiceProvider services, TurnRequest request, IInternalTurnSink sink)
    {
        var shortcut = services.GetService<BriefingShortcut>();
        if (shortcut is null)
        {
            return false;
        }

        var text = await shortcut.TryResolveAsync(request.UserId, request.UserMessage, _shutdown.Token);
        if (text is null)
        {
            return false;
        }

        var started = clock.GetTimestamp();
        var turnId = Guid.CreateVersion7();
        var store = services.GetRequiredService<IConversationStore>();
        await store.AppendMessageAsync(
            new AppendMessageRequest(
                request.ConversationId, request.UserId, MessageRole.User, request.UserMessage),
            _shutdown.Token);
        var assistant = await store.AppendMessageAsync(
            new AppendMessageRequest(
                request.ConversationId, request.UserId, MessageRole.Assistant, text),
            _shutdown.Token);

        using (LogContext.PushProperty("correlationId", turnId))
        {
            await sink.HandleAsync(new TurnAccepted(turnId), request, _shutdown.Token);
            await sink.HandleAsync(new TurnToken(text), request, _shutdown.Token);
            await sink.HandleAsync(
                new TurnFinished(TurnOutcome.Completed, assistant.Id, clock.GetElapsedTime(started)),
                request,
                _shutdown.Token);
        }

        return true;
    }

    private Task[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _running];
        }
    }

    public void Dispose()
    {
        _shutdown.Dispose();
    }
}
