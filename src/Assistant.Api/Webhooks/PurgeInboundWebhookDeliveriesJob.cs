using Assistant.Abstractions.Scheduling;
using Assistant.Abstractions.Webhooks;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Job <c>webhooks.purge_deliveries</c>. Deletes deliveries older than 24 hours and
/// enqueues the next local day under its own idempotency key.
/// </summary>
public sealed class PurgeInboundWebhookDeliveriesJob(
    IInboundWebhookDeliveryStore store,
    IJobScheduler scheduler,
    TimeProvider clock) : IJobHandler
{
    public const string KindName = "webhooks.purge_deliveries";

    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public string Kind => KindName;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = clock.GetUtcNow();
        await store.PurgeAsync(now - Retention, ct);
        var next = InboundWebhookPurgeSchedule.NextRun(now);
        await scheduler.ScheduleAsync(
            new JobSpec(KindName, next, "{}", InboundWebhookPurgeSchedule.KeyFor(next)),
            ct);
        return new JobOutcome.Succeeded();
    }
}

/// <summary>Local 04:30 in America/Argentina/Buenos_Aires. Key <c>webhooks.purge_deliveries:{yyyy-MM-dd}</c>.</summary>
internal static class InboundWebhookPurgeSchedule
{
    public static readonly TimeOnly LocalTime = new(4, 30);

    public const string TimeZoneId = "America/Argentina/Buenos_Aires";

    public static DateTimeOffset TodaySlot(DateTimeOffset utcNow)
    {
        var zone = Zone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        return ToUtc(localDate.ToDateTime(LocalTime), zone);
    }

    public static DateTimeOffset NextRun(DateTimeOffset utcNow)
    {
        var slot = TodaySlot(utcNow);
        if (slot > utcNow)
        {
            return slot;
        }

        var zone = Zone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var tomorrow = DateOnly.FromDateTime(local.DateTime).AddDays(1);
        return ToUtc(tomorrow.ToDateTime(LocalTime), zone);
    }

    public static string KeyFor(DateTimeOffset slotUtc)
    {
        var local = TimeZoneInfo.ConvertTime(slotUtc, Zone());
        return $"webhooks.purge_deliveries:{DateOnly.FromDateTime(local.DateTime):yyyy-MM-dd}";
    }

    private static TimeZoneInfo Zone()
        => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo zone)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified),
            zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
