using System.Text.Json;
using Assistant.Abstractions.Notifications;

namespace Assistant.Abstractions.Reminders;

/// <summary>
/// Snapshot of a reminder at the moment it fires.
/// </summary>
/// <param name="ReminderId">Reminder id.</param>
/// <param name="UserId">User of the reminder.</param>
/// <param name="Text">Reminder text.</param>
/// <param name="Status">Current status (must be "active").</param>
/// <param name="NextOccurrenceUtc">Expected next occurrence (UTC).</param>
/// <param name="RecurrenceRule">Recurrence rule (simplified RRULE).</param>
/// <param name="TimeZone">Canonical IANA id stored on the reminder.</param>
/// <param name="LocalStart">Civil start, without a zone.</param>
/// <param name="OccurrencesFired">Occurrences already fired before this attempt.</param>
/// <param name="OriginChannel">Channel the reminder was created from.</param>
/// <param name="OriginConversationId">Conversation to deep-link back to.</param>
/// <param name="SnoozeCount">Snoozes already taken for the current occurrence.</param>
public sealed record ReminderFiringSnapshot(
    Guid ReminderId,
    Guid UserId,
    string Text,
    string Status,
    DateTimeOffset? NextOccurrenceUtc,
    string? RecurrenceRule,
    string TimeZone,
    DateTime LocalStart,
    int OccurrencesFired,
    string OriginChannel,
    Guid? OriginConversationId,
    int SnoozeCount);

/// <summary>
/// Firing of a reminder (atomic transaction).
/// </summary>
/// <param name="ReminderId">Reminder id.</param>
/// <param name="OccurrenceUtc">Occurrence being fired (UTC).</param>
/// <param name="Notification">Notification to create (null when the occurrence is skipped because it is late).</param>
/// <param name="NextOccurrenceUtc">Next occurrence (null means a final status).</param>
/// <param name="FinalStatusIfNoNext">Final status when there is no next occurrence ("completed" or "missed").</param>
/// <param name="RecurrenceRule">Upgraded RRULE to store, or null when the stored text already matches.</param>
public sealed record ReminderFiring(
    Guid ReminderId,
    DateTimeOffset OccurrenceUtc,
    NotificationRequest? Notification,
    DateTimeOffset? NextOccurrenceUtc,
    string FinalStatusIfNoNext,
    string? RecurrenceRule = null);

/// <summary>
/// Store for firing reminders.
/// </summary>
public interface IReminderFiringStore
{
    /// <summary>
    /// Loads the reminder for firing (with <c>FOR UPDATE</c>).
    /// </summary>
    /// <param name="reminderId">Reminder id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Reminder snapshot, or null when it does not exist or is not active.</returns>
    public ValueTask<ReminderFiringSnapshot?> LoadForFiringAsync(Guid reminderId, CancellationToken ct);

    /// <summary>
    /// Commits the firing in one atomic transaction:
    /// creates the notification (when one applies),
    /// schedules the dispatch job,
    /// updates the reminder's next occurrence,
    /// schedules the next <c>reminder.fire</c> (when there is a next occurrence),
    /// or marks the final status (completed or missed).
    /// </summary>
    /// <param name="firing">Firing to commit.</param>
    /// <param name="ct">Cancellation token.</param>
    public ValueTask CommitFiringAsync(ReminderFiring firing, CancellationToken ct);

    /// <summary>Loads a reminder whether or not it is still active. Null when it does not exist.</summary>
    public ValueTask<ReminderFiringSnapshot?> LoadAsync(Guid reminderId, CancellationToken ct);

    /// <summary>
    /// Applies a notification button and, for a snooze, schedules <c>reminder.snooze</c>
    /// in the same transaction.
    /// </summary>
    /// <param name="maxSnoozes">Maximum snoozes of the current occurrence.</param>
    /// <param name="snoozeDelay">How far ahead a snooze job runs.</param>
    public ValueTask<NotificationActionResult> ApplyNotificationActionAsync(
        Guid userId,
        Guid notificationId,
        string actionId,
        int maxSnoozes,
        TimeSpan snoozeDelay,
        CancellationToken ct);
}

/// <summary>Identity of a <c>reminder.snooze</c> job. The key is <c>reminder:{id}:snooze:{n}</c>.</summary>
public static class ReminderSnooze
{
    /// <summary>Job kind handled by the snooze package.</summary>
    public const string JobKind = "reminder.snooze";

    public const string ActionId = "snooze:10m";

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Idempotency key for snooze number <paramref name="snoozeNumber"/> (1-based).</summary>
    public static string IdempotencyKey(Guid reminderId, int snoozeNumber)
        => FormattableString.Invariant($"reminder:{reminderId:D}:snooze:{snoozeNumber}");

    /// <summary>JSON payload <c>{reminderId, snoozeNumber}</c>.</summary>
    public static string PayloadJson(Guid reminderId, int snoozeNumber)
        => JsonSerializer.Serialize(new SnoozePayload(reminderId, snoozeNumber), PayloadJsonOptions);

    private sealed record SnoozePayload(Guid ReminderId, int SnoozeNumber);
}
