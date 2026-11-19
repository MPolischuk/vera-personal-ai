using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Scheduling;
using Assistant.Core.Conversations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// Job <c>proactive.heartbeat</c>. Outside the open window it moves to 08:00.
/// A conversation in progress moves it five minutes ahead and does not call the model.
/// </summary>
public sealed partial class HeartbeatJob(
    HeartbeatRunner runner,
    IConversationStore conversations,
    IActiveTurns activeTurns,
    IOptions<ProactiveOptions> options,
    TimeProvider clock,
    ILogger<HeartbeatJob> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public string Kind => HeartbeatJobs.Kind;

    public async ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryRead(context.PayloadJson, out var userId))
        {
            HeartbeatJobLog.PayloadInvalid(logger, context.JobId);
            return new JobOutcome.Fail("payload_invalid");
        }

        var settings = options.Value;
        var window = Window(settings);
        var every = settings.HeartbeatEvery < 1 ? HeartbeatJobs.EveryMinutes : settings.HeartbeatEvery;
        var now = clock.GetUtcNow();
        if (SuggestionFilter.IsQuiet(HeartbeatSchedule.LocalTime(now), window.QuietStart, window.QuietEnd))
        {
            return new JobOutcome.Reschedule(HeartbeatSchedule.NextOpen(now, window.QuietEnd));
        }

        var rows = await conversations.ListAsync(userId, 0, HeartbeatJobs.ConversationScan, ct);
        foreach (var row in rows)
        {
            if (activeTurns.IsInProgress(row.Id))
            {
                HeartbeatJobLog.Postponed(logger, userId);
                return new JobOutcome.Reschedule(now.AddMinutes(HeartbeatJobs.PostponeMinutes));
            }
        }

        Guid? latest = rows.Count == 0 ? null : rows[0].Id;
        await runner.RunAsync(userId, latest, window, ct);
        return new JobOutcome.Reschedule(HeartbeatSchedule.Next(now, window.QuietStart, window.QuietEnd, every));
    }

    private static SuggestionWindow Window(ProactiveOptions settings)
    {
        var start = Parse(settings.QuietStart, SuggestionWindow.Default.QuietStart);
        var end = Parse(settings.QuietEnd, SuggestionWindow.Default.QuietEnd);
        var max = settings.MaxPerDay < 1 ? SuggestionWindow.Default.MaxPerDay : settings.MaxPerDay;
        return new SuggestionWindow(start, end, max);
    }

    private static TimeOnly Parse(string? text, TimeOnly fallback)
        => TimeOnly.TryParseExact(
            text?.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : fallback;

    private static bool TryRead(string json, out Guid userId)
    {
        userId = Guid.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<HeartbeatJobPayload>(json, PayloadJson);
            if (parsed is null || parsed.UserId == Guid.Empty)
            {
                return false;
            }

            userId = parsed.UserId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Section <c>Proactive:Suggestions</c>. Defaults match the heartbeat, not reminder quiet hours.</summary>
public sealed class ProactiveOptions
{
    public const string SectionName = "Proactive:Suggestions";

    public string QuietStart { get; set; } = "22:00";

    public string QuietEnd { get; set; } = "08:00";

    public int MaxPerDay { get; set; } = 3;

    /// <summary>Minutes between pulses. The next run is the next boundary of this size.</summary>
    public int HeartbeatEvery { get; set; } = 30;
}

internal static partial class HeartbeatJobLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat job {JobId} has an invalid payload.")]
    public static partial void PayloadInvalid(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Heartbeat for user {UserId} waits because a turn is in progress.")]
    public static partial void Postponed(ILogger logger, Guid userId);
}
