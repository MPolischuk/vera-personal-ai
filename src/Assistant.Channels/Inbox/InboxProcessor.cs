using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Assistant.Abstractions.Channels;
using Assistant.Channels.Pairing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Inbox;

/// <summary>
/// One in-memory queue per <c>(channel, external_peer_id)</c> and a global parallelism cap.
/// Empty queues are dropped. Work runs after the inbox insert has committed.
/// </summary>
public sealed class InboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IChannelIngressTelemetry _telemetry;
    private readonly ILogger<InboxProcessor> _logger;
    private readonly SemaphoreSlim _parallel;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, PeerLane> _lanes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _drains = new();

    public InboxProcessor(
        IServiceScopeFactory scopes,
        IChannelIngressTelemetry telemetry,
        IOptions<InboxOptions> options,
        ILogger<InboxProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _telemetry = telemetry;
        _logger = logger;
        var parallelism = options.Value.InboxParallelism < 1 ? 1 : options.Value.InboxParallelism;
        _parallel = new SemaphoreSlim(parallelism, parallelism);
    }

    /// <summary>Queues one committed inbox id behind its peer. Does not wait for the handler.</summary>
    public void Enqueue(long inboxId, string peerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerKey);
        var lane = _lanes.GetOrAdd(peerKey, static _ => new PeerLane());
        var start = false;
        lock (lane.Gate)
        {
            if (!lane.Queue.Writer.TryWrite(inboxId))
            {
                return;
            }

            if (!lane.Draining)
            {
                lane.Draining = true;
                start = true;
            }
        }

        if (start)
        {
            StartDrain(peerKey, lane);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var registration = stoppingToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), _stop);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stop.CancelAsync();
        var pending = _drains.Keys.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _stop.Dispose();
        _parallel.Dispose();
        base.Dispose();
    }

    private void StartDrain(string peerKey, PeerLane lane)
    {
        var drain = DrainAsync(peerKey, lane);
        _drains.TryAdd(drain, 0);
        _ = drain.ContinueWith(
            static (task, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(task, out _),
            _drains,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DrainAsync(string peerKey, PeerLane lane)
    {
        var acquired = false;
        try
        {
            await _parallel.WaitAsync(_stop.Token);
            acquired = true;
            while (true)
            {
                long inboxId;
                lock (lane.Gate)
                {
                    if (!lane.Queue.Reader.TryRead(out inboxId))
                    {
                        lane.Draining = false;
                        _lanes.TryRemove(new KeyValuePair<string, PeerLane>(peerKey, lane));
                        return;
                    }
                }

                await ProcessAsync(inboxId);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            lock (lane.Gate)
            {
                lane.Draining = false;
            }
        }
        finally
        {
            if (acquired)
            {
                _parallel.Release();
            }
        }
    }

    private async Task ProcessAsync(long inboxId)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IChannelInboxStore>();
        var handler = scope.ServiceProvider.GetRequiredService<IInboxItemHandler>();
        var item = await store.FindAsync(inboxId, _stop.Token);
        if (item is null)
        {
            return;
        }

        using var activity = ChannelsTelemetry.ActivitySource.StartActivity("channel.inbound", ActivityKind.Internal);
        activity?.SetTag("channel", item.Envelope.Channel.Value);
        activity?.SetTag("inbox.id", item.InboxId);

        var marked = false;
        try
        {
            await store.MarkAsync(inboxId, "processing", null, _stop.Token);
            marked = true;
            var pairing = scope.ServiceProvider.GetService<PairingInboxStep>();
            if (pairing is not null)
            {
                var admission = PairingAdmissionReader.Read(item.Envelope);
                if (admission.Kind == PairingAdmissionKind.Ignore)
                {
                    await store.MarkAsync(inboxId, "ignored", null, _stop.Token);
                    marked = false;
                    _telemetry.Inbound(item.Envelope.Channel, "ignored");
                    LogFinished(item, "ignored");
                    return;
                }

                if (admission.Kind == PairingAdmissionKind.Pair)
                {
                    var result = await pairing.ApplyAsync(item.Envelope, admission, _stop.Token);
                    if (!result.ContinueToHandler)
                    {
                        await store.MarkAsync(inboxId, "ignored", null, _stop.Token);
                        marked = false;
                        _telemetry.Inbound(item.Envelope.Channel, result.Metric);
                        LogFinished(item, "ignored");
                        return;
                    }
                }
            }

            var outcome = await handler.HandleAsync(item, _stop.Token);
            var (status, error, metric) = Map(outcome);
            await store.MarkAsync(inboxId, status, error, _stop.Token);
            marked = false;
            _telemetry.Inbound(item.Envelope.Channel, metric);
            LogFinished(item, status);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            if (marked)
            {
                await ResetToReceivedAsync(store, inboxId);
            }

            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            InboxLog.HandlerFailed(_logger, inboxId, exception.GetType().Name);
            try
            {
                await store.MarkAsync(inboxId, "failed", "handler_fault", CancellationToken.None);
            }
            catch (Exception markException)
            {
                InboxLog.HandlerFailed(_logger, inboxId, markException.GetType().Name);
            }

            _telemetry.Inbound(item.Envelope.Channel, "failed");
        }
    }

    private void LogFinished(InboundItem item, string status)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var peerRef = ChannelPeerLogRef.Format(item.Envelope.Channel, item.Envelope.ExternalPeerId);
        InboxLog.Finished(_logger, item.InboxId, peerRef, status);
    }

    private static async Task ResetToReceivedAsync(IChannelInboxStore store, long inboxId)
    {
        try
        {
            using var reset = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await store.MarkAsync(inboxId, "received", null, reset.Token);
        }
        catch (Exception)
        {
            // The row stays processing. Startup recovery only picks it up once it is stale.
        }
    }

    private static (string Status, string? Error, string Metric) Map(InboundOutcome outcome) => outcome switch
    {
        InboundOutcome.TurnStarted => ("done", null, "turn"),
        InboundOutcome.CommandHandled => ("done", null, "command"),
        InboundOutcome.ConfirmationResolved => ("done", null, "confirmation"),
        InboundOutcome.Ignored => ("ignored", null, "ignored"),
        InboundOutcome.RejectedPending => ("ignored", "rejected_pending", "ignored"),
        InboundOutcome.Failed => ("failed", "failed", "failed"),
        _ => ("failed", "failed", "failed"),
    };

    private sealed class PeerLane
    {
        public object Gate { get; } = new();

        public Channel<long> Queue { get; } = System.Threading.Channels.Channel.CreateUnbounded<long>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

        public bool Draining;
    }
}
