using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Scheduling;
using Assistant.Core.Events;
using Assistant.Extensions.Reminders.Events;
using Assistant.Extensions.Reminders.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Reminders.Firing;

/// <summary>
/// Job handler <c>reminder.fire</c>. Decides lateness, writes one notification at most,
/// and schedules the next occurrence. It does not call a language model.
/// </summary>
public sealed class ReminderFireHandler(
    IReminderFiringStore store,
    IEventBus events,
    IChannelAuditSink audit,
    TimeProvider clock,
    IOptions<RemindersOptions> options,
    ILogger<ReminderFireHandler> logger) : IJobHandler
{
    public const string Title = "Recordatorio";

    private const int WalkCap = 5_000;

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<OutboundAction> Actions =
    [
        new("done", "Listo"),
        new("snooze:10m", "+10 min"),
    ];

    public string Kind => ReminderFire.JobKind;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryRead(context.PayloadJson, out var reminderId, out var occurrence))
        {
            ReminderFireLog.PayloadInvalid(logger, context.JobId);
            return new JobOutcome.Fail("payload_invalid");
        }

        var snapshot = await store.LoadForFiringAsync(reminderId, ct);
        var scheduled = ReminderFire.NormalizeUtc(occurrence);
        if (snapshot is null
            || snapshot.NextOccurrenceUtc is not DateTimeOffset due
            || ReminderFire.NormalizeUtc(due) != scheduled)
        {
            ReminderFireLog.Stale(logger, reminderId);
            return new JobOutcome.Succeeded();
        }

        string? ruleToStore = null;
        if (!string.IsNullOrWhiteSpace(snapshot.RecurrenceRule))
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(snapshot.TimeZone, out var zone))
            {
                ReminderFireLog.RecurrenceInvalid(logger, snapshot.ReminderId);
                return new JobOutcome.Fail("recurrence_invalid");
            }

            var upgraded = RecurrenceSpec.UpgradeStoredRule(
                snapshot.RecurrenceRule, snapshot.LocalStart, zone);
            if (!RecurrenceSpec.TryParseRule(upgraded, zone, out _))
            {
                ReminderFireLog.RecurrenceInvalid(logger, snapshot.ReminderId);
                return new JobOutcome.Fail("recurrence_invalid");
            }

            if (!string.Equals(upgraded, snapshot.RecurrenceRule, StringComparison.Ordinal))
            {
                ruleToStore = upgraded;
                snapshot = snapshot with { RecurrenceRule = upgraded };
            }
        }

        var limits = options.Value;
        var now = clock.GetUtcNow();
        var plan = Plan(snapshot, scheduled, now, limits);
        var notification = plan.Deliver is null
            ? null
            : Notice(snapshot, plan.Deliver.Value, plan.Decision);
        await store.CommitFiringAsync(
            new ReminderFiring(
                snapshot.ReminderId,
                scheduled,
                notification,
                plan.NextUtc,
                plan.FinalStatus,
                ruleToStore),
            ct);

        await PublishDueAsync(snapshot, scheduled, plan, ct);
        await AuditAsync(snapshot, plan, ct);
        ReminderTelemetry.Record(
            snapshot.OriginChannel,
            recurring: !string.IsNullOrWhiteSpace(snapshot.RecurrenceRule),
            fired: plan.Deliver is not null);
        if (plan.Deliver is null)
        {
            ReminderFireLog.Missed(logger, snapshot.ReminderId);
        }
        else
        {
            ReminderFireLog.Fired(logger, snapshot.ReminderId);
        }

        return new JobOutcome.Succeeded();
    }

    private static bool TryRead(string json, out Guid reminderId, out DateTimeOffset occurrence)
    {
        reminderId = Guid.Empty;
        occurrence = default;
        try
        {
            var payload = JsonSerializer.Deserialize<FirePayload>(json, PayloadJson);
            if (payload is null || payload.ReminderId == Guid.Empty)
            {
                return false;
            }

            reminderId = payload.ReminderId;
            occurrence = payload.OccurrenceUtc;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static FirePlan Plan(
        ReminderFiringSnapshot snapshot,
        DateTimeOffset scheduled,
        DateTimeOffset now,
        RemindersOptions limits)
    {
        if (string.IsNullOrWhiteSpace(snapshot.RecurrenceRule)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(snapshot.TimeZone, out var zone)
            || !RecurrenceSpec.TryParseRule(snapshot.RecurrenceRule, zone, out var spec)
            || spec is null)
        {
            return PlanSingle(scheduled, now, limits);
        }

        var overdue = new List<DateTimeOffset>();
        DateTimeOffset? future = null;
        foreach (var utc in Enumerate(snapshot, spec, zone))
        {
            var normalized = ReminderFire.NormalizeUtc(utc);
            if (normalized < scheduled)
            {
                continue;
            }

            if (normalized > now)
            {
                future = normalized;
                break;
            }

            overdue.Add(normalized);
        }

        if (overdue.Count == 0)
        {
            var early = LatenessPolicy.Decide(now - scheduled, limits);
            if (early.Kind == ReminderLateness.Missed)
            {
                var skipTo = future is DateTimeOffset upcoming && upcoming > now
                    ? upcoming
                    : FirstAfter(snapshot, spec, zone, now);
                return new FirePlan(
                    null,
                    early,
                    skipTo,
                    skipTo is null ? ReminderStatus.Missed : ReminderStatus.Completed);
            }

            var next = future is DateTimeOffset upcomingAfter && upcomingAfter > scheduled
                ? upcomingAfter
                : FirstAfter(snapshot, spec, zone, scheduled);
            return new FirePlan(scheduled, early, next, ReminderStatus.Completed);
        }

        var last = overdue[^1];
        var decision = LatenessPolicy.Decide(now - last, limits);
        if (decision.Kind == ReminderLateness.Missed)
        {
            return new FirePlan(
                null,
                decision,
                future,
                future is null ? ReminderStatus.Missed : ReminderStatus.Completed);
        }

        return new FirePlan(last, decision, future, ReminderStatus.Completed);
    }

    private static FirePlan PlanSingle(DateTimeOffset scheduled, DateTimeOffset now, RemindersOptions limits)
    {
        var decision = LatenessPolicy.Decide(now - scheduled, limits);
        if (decision.Kind == ReminderLateness.Missed)
        {
            return new FirePlan(null, decision, null, ReminderStatus.Missed);
        }

        return new FirePlan(scheduled, decision, null, ReminderStatus.Completed);
    }

    private static DateTimeOffset? FirstAfter(
        ReminderFiringSnapshot snapshot,
        RecurrenceSpec spec,
        TimeZoneInfo zone,
        DateTimeOffset instant)
    {
        foreach (var utc in Enumerate(snapshot, spec, zone))
        {
            var normalized = ReminderFire.NormalizeUtc(utc);
            if (normalized > instant)
            {
                return normalized;
            }
        }

        return null;
    }

    private static IEnumerable<DateTimeOffset> Enumerate(
        ReminderFiringSnapshot snapshot,
        RecurrenceSpec spec,
        TimeZoneInfo zone)
    {
        var after = snapshot.LocalStart.AddTicks(-1);
        for (var index = 0; index < WalkCap; index++)
        {
            var local = RecurrenceCalculator.NextLocal(snapshot.LocalStart, spec, after, occurrencesFired: 0);
            if (local is null)
            {
                yield break;
            }

            yield return ToUtc(local.Value, zone);
            after = local.Value;
        }
    }

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            var cursor = local;
            for (var minute = 0; minute < 180; minute++)
            {
                cursor = cursor.AddMinutes(1);
                if (!zone.IsInvalidTime(cursor))
                {
                    local = new DateTime(
                        cursor.Year, cursor.Month, cursor.Day, cursor.Hour, cursor.Minute, 0, DateTimeKind.Unspecified);
                    break;
                }
            }
        }

        if (zone.IsAmbiguousTime(local))
        {
            return zone.GetAmbiguousTimeOffsets(local)
                .Select(offset => new DateTimeOffset(local, offset))
                .OrderBy(instant => instant.UtcDateTime)
                .First();
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static NotificationRequest Notice(
        ReminderFiringSnapshot snapshot,
        DateTimeOffset delivered,
        LatenessDecision decision)
    {
        var body = decision.Kind == ReminderLateness.Late
            ? LateBody(snapshot, delivered)
            : snapshot.Text;
        return new NotificationRequest(
            snapshot.UserId,
            NotificationKind.Reminder,
            ReminderFire.IdempotencyKey(snapshot.ReminderId, delivered),
            Title,
            body,
            delivered,
            ChannelOf(snapshot.OriginChannel),
            snapshot.OriginConversationId,
            Actions,
            "reminder",
            snapshot.ReminderId.ToString("D"),
            NotificationContentTrust.Trusted,
            decision.AllowDuringQuietHours);
    }

    private static string LateBody(ReminderFiringSnapshot snapshot, DateTimeOffset delivered)
    {
        var time = "??:??";
        if (TimeZoneInfo.TryFindSystemTimeZoneById(snapshot.TimeZone, out var zone))
        {
            var local = TimeZoneInfo.ConvertTime(delivered, zone);
            time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return FormattableString.Invariant(
            $"Se me pasó la hora: {snapshot.Text} (era a las {time}).");
    }

    private async ValueTask PublishDueAsync(
        ReminderFiringSnapshot snapshot,
        DateTimeOffset scheduled,
        FirePlan plan,
        CancellationToken ct)
    {
        try
        {
            await events.PublishAsync(
                new ReminderDue(
                    snapshot.ReminderId,
                    snapshot.UserId,
                    scheduled,
                    plan.Deliver is not null,
                    plan.NextUtc),
                ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReminderFireLog.PublishFailed(logger, exception, snapshot.ReminderId);
        }
    }

    private async ValueTask AuditAsync(ReminderFiringSnapshot snapshot, FirePlan plan, CancellationToken ct)
    {
        var fired = plan.Deliver is not null;
        try
        {
            await audit.RecordAsync(
                new ChannelAuditEntry(
                    fired ? "reminder.fired" : "reminder.missed",
                    ChannelOf(snapshot.OriginChannel),
                    null,
                    snapshot.OriginConversationId,
                    null,
                    fired
                        ? plan.Decision.Kind == ReminderLateness.Late ? "late" : "on_time"
                        : plan.NextUtc is null ? "missed" : "skipped"),
                ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReminderFireLog.AuditFailed(logger, exception, snapshot.ReminderId);
        }
    }

    private static ChannelId ChannelOf(string origin)
        => origin switch
        {
            "hub" => ChannelId.Hub,
            "webpush" => ChannelId.WebPush,
            "telegram" => ChannelId.Telegram,
            "whatsapp" => ChannelId.WhatsApp,
            _ => new ChannelId(string.IsNullOrWhiteSpace(origin) ? "hub" : origin),
        };

    private sealed record FirePayload(Guid ReminderId, DateTimeOffset OccurrenceUtc);

    private sealed record FirePlan(
        DateTimeOffset? Deliver,
        LatenessDecision Decision,
        DateTimeOffset? NextUtc,
        string FinalStatus);
}
