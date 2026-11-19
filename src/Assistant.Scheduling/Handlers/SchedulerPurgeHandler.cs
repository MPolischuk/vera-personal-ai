using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Options;

namespace Assistant.Scheduling.Handlers;

/// <summary>
/// Deletes old terminal jobs and reschedules itself for the next local 04:20.
/// Seeded at startup as <c>purge:jobs:{date}</c>.
/// </summary>
public sealed class SchedulerPurgeHandler(
    IJobStore store,
    TimeProvider clock,
    IOptions<SchedulerOptions> options) : IJobHandler
{
    public const string KindName = "scheduler.purge";

    public string Kind => KindName;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var current = options.Value;
        var now = clock.GetUtcNow();
        await store.PurgeAsync(now, current.SucceededRetention, current.FailedRetention, ct);
        return new JobOutcome.Reschedule(PurgeSchedule.NextRun(now, current));
    }
}

/// <summary>Local 04:20 slot and the idempotency key <c>purge:jobs:{yyyy-MM-dd}</c>.</summary>
internal static class PurgeSchedule
{
    public static DateTimeOffset TodaySlot(DateTimeOffset utcNow, SchedulerOptions options)
    {
        var zone = Zone(options);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        return ToUtc(localDate.ToDateTime(options.PurgeLocalTime), zone);
    }

    public static DateTimeOffset NextRun(DateTimeOffset utcNow, SchedulerOptions options)
    {
        var slot = TodaySlot(utcNow, options);
        if (slot > utcNow)
        {
            return slot;
        }

        var zone = Zone(options);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var tomorrow = DateOnly.FromDateTime(local.DateTime).AddDays(1);
        return ToUtc(tomorrow.ToDateTime(options.PurgeLocalTime), zone);
    }

    public static string KeyFor(DateTimeOffset slotUtc, SchedulerOptions options)
    {
        var local = TimeZoneInfo.ConvertTime(slotUtc, Zone(options));
        return $"purge:jobs:{DateOnly.FromDateTime(local.DateTime):yyyy-MM-dd}";
    }

    private static TimeZoneInfo Zone(SchedulerOptions options)
        => TimeZoneInfo.FindSystemTimeZoneById(options.PurgeTimeZoneId);

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo zone)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified),
            zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
