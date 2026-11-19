using Assistant.Abstractions.Channels;

namespace Assistant.Abstractions.Notifications;

/// <summary>
/// Kind of notification.
/// </summary>
public enum NotificationKind
{
    /// <summary>Reminder (stage 4b).</summary>
    Reminder,

    /// <summary>Background task finished (stage 6).</summary>
    TaskFinished,

    /// <summary>Proactive notice on Vera's initiative (stage 8).</summary>
    Proactive,

    /// <summary>Digest of external sources (stage 8, always untrusted).</summary>
    ExternalDigest,

    /// <summary>Confirmation from another channel (stage 9).</summary>
    ConfirmationRequest,

    /// <summary>System notification (for example, "the Telegram bot was unlinked").</summary>
    System
}

/// <summary>
/// Trust level of notification content.
/// </summary>
public enum NotificationContentTrust
{
    /// <summary>Trusted content produced by the system.</summary>
    Trusted,

    /// <summary>Untrusted content from an external origin.</summary>
    Untrusted
}

/// <summary>
/// Request to notify the user.
/// </summary>
/// <param name="UserId">Recipient user.</param>
/// <param name="Kind">Notification kind.</param>
/// <param name="DedupKey">Idempotency key: (UserId, DedupKey) is unique.</param>
/// <param name="Title">Title (at most 60 characters).</param>
/// <param name="Body">Message body (at most 1,000 characters; push trims it to 240).</param>
/// <param name="DueAt">Expected delivery time (used to measure lateness on reminders).</param>
/// <param name="PreferredChannel">Preferred channel by affinity.</param>
/// <param name="ConversationId">Destination conversation for the deep link.</param>
/// <param name="Actions">Interactive buttons.</param>
/// <param name="SourceType">Origin type (for example, "reminder", "task", "digest:gmail").</param>
/// <param name="SourceId">Origin id.</param>
/// <param name="Trust">Content trust level.</param>
/// <param name="AllowDuringQuietHours">Whether to deliver during quiet hours (explicit override).</param>
public sealed record NotificationRequest(
    Guid UserId,
    NotificationKind Kind,
    string DedupKey,
    string Title,
    string Body,
    DateTimeOffset? DueAt = null,
    ChannelId? PreferredChannel = null,
    Guid? ConversationId = null,
    IReadOnlyList<OutboundAction>? Actions = null,
    string? SourceType = null,
    string? SourceId = null,
    NotificationContentTrust Trust = NotificationContentTrust.Trusted,
    bool AllowDuringQuietHours = false);

/// <summary>
/// Receipt for a published notification.
/// </summary>
/// <param name="NotificationId">Notification id.</param>
/// <param name="Created">Whether a new row was created, or one already existed for the dedup key.</param>
public sealed record NotificationReceipt(Guid NotificationId, bool Created);

/// <summary>
/// Publisher of notifications.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Creates the notification (or returns the existing one for the dedup key) and schedules
    /// <c>notification.dispatch</c>. Idempotent. Does not deliver: delivery belongs to the job.
    /// </summary>
    /// <param name="request">Notification request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Notification receipt.</returns>
    public ValueTask<NotificationReceipt> PublishAsync(NotificationRequest request, CancellationToken ct);
}
