using Assistant.Abstractions.Channels;
using Assistant.Api.Channels;
using Assistant.Core.Confirmations;
using Assistant.Core.Conversations;
using Microsoft.Extensions.Options;
using ChannelTurnOrigin = Assistant.Abstractions.Channels.TurnOrigin;

namespace Assistant.Api.Realtime;

internal sealed class ConfirmationExpiryOptions
{
    public const string SectionName = "Assistant:Confirmations:Expiry";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 32;
}

/// <summary>
/// Persisted expiry (D6): batches open rows past <c>expires_at</c> and closes them
/// with the same deny path as an explicit rejection, outcome <c>expired</c>.
/// </summary>
internal sealed class ConfirmationExpiryService(
    IServiceScopeFactory scopes,
    TurnRunner runner,
    TimeProvider clock,
    IOptions<ConfirmationExpiryOptions> options,
    ILogger<ConfirmationExpiryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = options.Value.PollInterval;
        if (poll < TimeSpan.FromMilliseconds(50))
        {
            poll = TimeSpan.FromMilliseconds(50);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                ApiLog.ConfirmationExpiryFailed(log, e);
            }

            try
            {
                await Task.Delay(poll, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ExpireBatchAsync(CancellationToken ct)
    {
        IReadOnlyList<PendingConfirmation> expired;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPendingConfirmationStore>();
            expired = await store.ListExpiredAsync(
                clock.GetUtcNow(), options.Value.BatchSize, ct);
        }

        foreach (var pending in expired)
        {
            ct.ThrowIfCancellationRequested();
            var channel = await TryCreateChannelSinkAsync(pending.ConversationId, ct);
            var request = TurnRequest.Expire(pending.ConversationId, pending.UserId, pending.Id);
            if (channel is null)
            {
                // No peer (or the directory cannot be activated): same path as before.
                await runner.ResumeAsync(request, connectionId: string.Empty);
                continue;
            }

            await runner.ResumeAsync(request, channel.Value.Origin, channel.Value.Sink);
        }
    }

    private async Task<(ChannelTurnOrigin Origin, IInternalTurnSink Sink)?> TryCreateChannelSinkAsync(
        Guid conversationId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        IChannelPeerDirectory directory;
        try
        {
            directory = scope.ServiceProvider.GetRequiredService<IChannelPeerDirectory>();
        }
        catch (InvalidOperationException e)
        {
            // The API test host removes the database context, so the persistence directory
            // cannot be activated. Treat that as "no peer" and keep today's hub path.
            ApiLog.ChannelPeerLookupFailed(log, e, conversationId);
            return null;
        }

        var peer = await directory.FindByConversationAsync(conversationId, ct);
        if (peer is null)
        {
            return null;
        }

        var factory = scope.ServiceProvider
            .GetServices<IChannelTurnSinkFactory>()
            .FirstOrDefault(candidate => candidate.Channel == peer.Channel);
        if (factory is null)
        {
            ApiLog.ChannelSinkFactoryMissing(log, peer.Channel.Value);
            return null;
        }

        return (new ChannelTurnOrigin(peer.Channel, peer.Id), new ChannelTurnSinkAdapter(factory.Create(peer)));
    }
}
