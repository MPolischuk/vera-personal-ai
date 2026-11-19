using System.Text.Json;

namespace Assistant.Abstractions.Reminders;

/// <summary>Stored status values of <c>reminders.status</c>.</summary>
public static class ReminderStatus
{
    public const string Active = "active";

    public const string Completed = "completed";

    public const string Cancelled = "cancelled";

    public const string Missed = "missed";
}

/// <summary>Who asked for the reminder.</summary>
/// <param name="TurnId">Turn that created it.</param>
/// <param name="ToolCallId">Tool call id when the source is a tool.</param>
/// <param name="Source"><c>tool</c> or <c>web</c>.</param>
public sealed record ReminderProvenance(Guid TurnId, string? ToolCallId, string Source);

/// <summary>A reminder row.</summary>
/// <param name="Id">Reminder id.</param>
/// <param name="UserId">Owner.</param>
/// <param name="Text">Text that will be shown, 1–500 characters.</param>
/// <param name="Status">One of <see cref="ReminderStatus"/>.</param>
/// <param name="TimeZone">Canonical IANA id.</param>
/// <param name="LocalStart">Civil start, without a zone.</param>
/// <param name="NextOccurrenceAt">Next UTC occurrence, or null when none remain.</param>
/// <param name="RecurrenceRule">Server-built RRULE subset, or null for a single shot.</param>
/// <param name="OccurrencesFired">How many occurrences have already fired (<c>COUNT</c>).</param>
/// <param name="LastFiredAt">Last fire time, UTC.</param>
/// <param name="OriginChannel">Channel the request came from (<c>hub</c>, <c>telegram</c>, …).</param>
/// <param name="OriginConversationId">Conversation to deep-link back to.</param>
/// <param name="Provenance">How the row was created.</param>
/// <param name="SnoozeCount">Snoozes of the current occurrence.</param>
/// <param name="CreatedAt">Creation time, UTC.</param>
/// <param name="UpdatedAt">Last update, UTC.</param>
/// <param name="CancelledAt">Cancellation time, UTC.</param>
/// <param name="CompletedAt">Completion time, UTC.</param>
public sealed record Reminder(
    Guid Id,
    Guid UserId,
    string Text,
    string Status,
    string TimeZone,
    DateTime LocalStart,
    DateTimeOffset? NextOccurrenceAt,
    string? RecurrenceRule,
    int OccurrencesFired,
    DateTimeOffset? LastFiredAt,
    string OriginChannel,
    Guid? OriginConversationId,
    ReminderProvenance Provenance,
    int SnoozeCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// A reminder the server already validated. The store does not reinterpret the clock.
/// </summary>
/// <param name="UserId">Owner.</param>
/// <param name="Text">Text that will be shown.</param>
/// <param name="TimeZone">Canonical IANA id.</param>
/// <param name="LocalStart">Civil start, without a zone.</param>
/// <param name="NextOccurrenceAt">First UTC occurrence.</param>
/// <param name="RecurrenceRule">Server-built rule, or null.</param>
/// <param name="OriginChannel">Channel id value.</param>
/// <param name="OriginConversationId">Originating conversation.</param>
/// <param name="Provenance">Write provenance.</param>
public sealed record NewReminder(
    Guid UserId,
    string Text,
    string TimeZone,
    DateTime LocalStart,
    DateTimeOffset NextOccurrenceAt,
    string? RecurrenceRule,
    string OriginChannel,
    Guid? OriginConversationId,
    ReminderProvenance Provenance);

/// <summary>Why a write was refused.</summary>
public enum ReminderRejection
{
    /// <summary>The user already has the maximum number of active reminders.</summary>
    TooManyActive = 1,

    /// <summary>No row matches the id for this user.</summary>
    NotFound = 2,

    /// <summary>The row exists but is not active.</summary>
    NotActive = 3,
}

/// <summary>Result of creating a reminder and its first job.</summary>
public abstract record CreateReminderResult
{
    private CreateReminderResult()
    {
    }

    /// <summary>The row and the job were committed.</summary>
    /// <param name="Reminder">Stored reminder.</param>
    /// <param name="ShortRef">Position among active reminders ordered by the next occurrence.</param>
    public sealed record Created(Reminder Reminder, int ShortRef) : CreateReminderResult;

    /// <summary>Nothing was written.</summary>
    /// <param name="Reason">Why.</param>
    public sealed record Rejected(ReminderRejection Reason) : CreateReminderResult;
}

/// <summary>Result of cancelling a reminder and its pending job.</summary>
public abstract record CancelReminderResult
{
    private CancelReminderResult()
    {
    }

    /// <summary>The row is cancelled and the pending job was cancelled when it was still scheduled.</summary>
    /// <param name="Reminder">Updated reminder.</param>
    public sealed record Cancelled(Reminder Reminder) : CancelReminderResult;

    /// <summary>Nothing was changed.</summary>
    /// <param name="Reason">Why.</param>
    public sealed record Rejected(ReminderRejection Reason) : CancelReminderResult;
}

/// <summary>Which rows <see cref="IReminderQueryService"/> returns.</summary>
public enum ReminderListScope
{
    /// <summary>Active reminders only.</summary>
    Active = 0,

    /// <summary>Missed reminders only.</summary>
    Missed = 1,

    /// <summary>Active and missed.</summary>
    All = 2,
}

/// <summary>One row prepared for a list. <paramref name="ShortRef"/> is set only for active rows.</summary>
/// <param name="Id">Reminder id.</param>
/// <param name="ShortRef">1-based position among active reminders, or null when the row is not active.</param>
/// <param name="Text">Reminder text.</param>
/// <param name="Status">Status value.</param>
/// <param name="TimeZone">Canonical IANA id.</param>
/// <param name="LocalStart">Civil start.</param>
/// <param name="NextOccurrenceAt">Next UTC occurrence.</param>
/// <param name="RecurrenceRule">Stored rule, or null.</param>
/// <param name="OriginChannel">Origin channel.</param>
/// <param name="UpdatedAt">Last update, UTC. The web keeps missed rows from the last 30 days with this.</param>
public sealed record ReminderListItem(
    Guid Id,
    int? ShortRef,
    string Text,
    string Status,
    string TimeZone,
    DateTime LocalStart,
    DateTimeOffset? NextOccurrenceAt,
    string? RecurrenceRule,
    string OriginChannel,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Identity of a <c>reminder.fire</c> job. The key is
/// <c>reminder:{id}:{occurrenceUtc:O}</c>.
/// </summary>
public static class ReminderFire
{
    /// <summary>Job kind handled by the firing package.</summary>
    public const string JobKind = "reminder.fire";

    /// <summary>Provenance source when a tool created the row.</summary>
    public const string SourceTool = "tool";

    /// <summary>Provenance source when the web created the row.</summary>
    public const string SourceWeb = "web";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Idempotency key of the job for one occurrence.</summary>
    /// <param name="reminderId">Reminder id.</param>
    /// <param name="occurrenceUtc">Occurrence, UTC.</param>
    /// <returns>The key.</returns>
    public static string IdempotencyKey(Guid reminderId, DateTimeOffset occurrenceUtc)
        => FormattableString.Invariant($"reminder:{reminderId:D}:{NormalizeUtc(occurrenceUtc):O}");

    /// <summary>JSON payload <c>{reminderId, occurrenceUtc}</c>.</summary>
    /// <param name="reminderId">Reminder id.</param>
    /// <param name="occurrenceUtc">Occurrence, UTC.</param>
    /// <returns>JSON text.</returns>
    public static string PayloadJson(Guid reminderId, DateTimeOffset occurrenceUtc)
        => JsonSerializer.Serialize(
            new ReminderFirePayload(reminderId, NormalizeUtc(occurrenceUtc)),
            PayloadOptions);

    /// <summary>
    /// Truncates to microseconds and UTC so the key built in memory matches the value read back
    /// from <c>timestamptz</c>.
    /// </summary>
    /// <param name="value">Any offset.</param>
    /// <returns>UTC truncated to microseconds.</returns>
    public static DateTimeOffset NormalizeUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcTicks - (utc.UtcTicks % 10);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed record ReminderFirePayload(Guid ReminderId, DateTimeOffset OccurrenceUtc);
}
