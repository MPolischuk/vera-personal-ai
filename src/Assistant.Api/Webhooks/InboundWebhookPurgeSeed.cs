using Assistant.Abstractions.Scheduling;

namespace Assistant.Api.Webhooks;

/// <summary>Seeds today's <c>webhooks.purge_deliveries</c> job. The same local day does not insert a second row.</summary>
internal sealed class InboundWebhookPurgeSeed(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<InboundWebhookPurgeSeed> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            var slot = InboundWebhookPurgeSchedule.TodaySlot(clock.GetUtcNow());
            await scheduler.ScheduleAsync(
                new JobSpec(
                    PurgeInboundWebhookDeliveriesJob.KindName,
                    slot,
                    "{}",
                    InboundWebhookPurgeSchedule.KeyFor(slot)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                var exceptionType = exception.GetType().Name;
                InboundWebhookLog.PurgeSeedFailed(logger, exceptionType);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
