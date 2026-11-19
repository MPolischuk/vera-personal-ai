using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Inbox;

/// <summary>
/// Re-enqueues <c>received</c> and stale <c>processing</c> rows, and seeds <c>channel.inbox.purge</c>.
/// </summary>
public sealed class InboxRecoveryService(
    IServiceScopeFactory scopes,
    InboxProcessor processor,
    TimeProvider clock,
    ILogger<InboxRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IChannelInboxStore>();
        var recovered = 0;
        var afterId = 0L;
        while (true)
        {
            var batch = await store.ListRecoverableAsync(afterId, limit: 100, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var item in batch)
            {
                processor.Enqueue(item.InboxId, InboxPeerKey.Format(item.Envelope));
                afterId = item.InboxId;
                recovered++;
            }
        }

        if (recovered > 0)
        {
            InboxLog.Recovered(logger, recovered);
        }

        try
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            var slot = InboxPurgeSchedule.TodaySlot(clock.GetUtcNow());
            await scheduler.ScheduleAsync(
                new JobSpec(
                    InboxPurgeHandler.KindName,
                    slot,
                    "{}",
                    InboxPurgeSchedule.KeyFor(slot)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            InboxLog.PurgeSeedFailed(logger, exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
