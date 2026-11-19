using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Scheduling;
using Assistant.Abstractions.Tasks;
using Assistant.Abstractions.Time;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// Runs one user automation. It does not call a model. The system briefing is a different job.
/// The task row is inserted already completed and does not arm a finish notice.
/// </summary>
public sealed partial class AutomationRunHandler(
    IAutomationStore automations,
    IAgentTaskStore tasks,
    IJobScheduler scheduler,
    IUserTimeZoneProvider timeZones,
    TimeProvider clock,
    ILogger<AutomationRunHandler> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public string Kind => AutomationJobs.RunKind;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        AutomationRunPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AutomationRunPayload>(context.PayloadJson, PayloadJson);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || payload.UserId == Guid.Empty || payload.AutomationId == Guid.Empty)
        {
            return new JobOutcome.Fail("payload_invalid");
        }

        var row = await automations.FindAsync(payload.UserId, payload.AutomationId, ct);
        if (row is null || row.Status != AutomationStatuses.Active || row.Cadence == AutomationCadence.SystemDaily)
        {
            return new JobOutcome.Succeeded();
        }

        var now = clock.GetUtcNow();
        var title = row.Title.Length <= 60 ? row.Title : row.Title[..60];
        await tasks.InsertAsync(
            new AgentTaskInsert(
                row.UserId,
                row.ConversationId,
                Guid.CreateVersion7(),
                AutomationJobs.TaskKey(row.Id, context.ScheduledFor),
                AutomationJobs.TaskKind,
                title,
                row.Instruction,
                1m,
                now.AddHours(1),
                true,
                null,
                AgentTaskStatuses.Completed,
                false,
                0m,
                null),
            now,
            ct);

        var next = await NextAsync(row, now, ct);
        if (next is DateTimeOffset zoned)
        {
            next = zoned.ToUniversalTime();
        }
        var status = next is null ? AutomationStatuses.Paused : AutomationStatuses.Active;
        await automations.SetNextRunAsync(row.UserId, row.Id, next, status, ct);
        if (next is DateTimeOffset due)
        {
            await scheduler.ScheduleAsync(
                new JobSpec(
                    AutomationJobs.RunKind,
                    due,
                    JsonSerializer.Serialize(new AutomationRunPayload(row.UserId, row.Id), PayloadJson),
                    AutomationJobs.Key(row.Id, due)),
                ct);
        }

        AutomationRunLog.Ran(logger, row.Id);
        return new JobOutcome.Succeeded();
    }

    private async ValueTask<DateTimeOffset?> NextAsync(Automation row, DateTimeOffset now, CancellationToken ct)
    {
        if (row.Cadence == AutomationCadence.Once)
        {
            return null;
        }

        if (row.Cadence == AutomationCadence.Interval && row.IntervalMinutes is int minutes)
        {
            return now.AddMinutes(minutes);
        }

        if (row.Cadence == AutomationCadence.Daily && AutomationRules.TryParseLocalTime(row.LocalTime, out var hhmm))
        {
            var zone = await timeZones.GetTimeZoneAsync(row.UserId, ct);
            var parsed = TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);
            var local = TimeZoneInfo.ConvertTime(now, zone);
            var candidate = new DateTime(
                local.Year, local.Month, local.Day, parsed.Hour, parsed.Minute, 0, DateTimeKind.Unspecified);
            if (new DateTimeOffset(candidate, zone.GetUtcOffset(candidate)) <= now)
            {
                candidate = candidate.AddDays(1);
            }

            return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
        }

        return null;
    }
}

internal static partial class AutomationRunLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Automation {AutomationId} ran.")]
    public static partial void Ran(ILogger logger, Guid automationId);
}
