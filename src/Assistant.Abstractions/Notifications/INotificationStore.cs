using Assistant.Abstractions.Channels;

namespace Assistant.Abstractions.Notifications;

/// <summary>Stored status values of <c>notifications.status</c>.</summary>
public static class NotificationStatuses
{
    public const string Pending = "pending";

    public const string Dispatching = "dispatching";

    public const string Deferred = "deferred";

    public const string Delivered = "delivered";

    public const string Acted = "acted";

    public const string Failed = "failed";

    public const string Cancelled = "cancelled";
}

/// <summary>
/// Notification row the router needs. Title and body stay out of logs.
/// </summary>
/// <param name="Id">Notification id.</param>
/// <param name="UserId">Recipient.</param>
/// <param name="Kind">Notification kind.</param>
/// <param name="Title">Title.</param>
/// <param name="Body">Body. Empty when the column is null.</param>
/// <param name="DueAt">Expected time, when the publisher set one.</param>
/// <param name="PreferredChannel">Affinity channel.</param>
/// <param name="ConversationId">Deep-link conversation.</param>
/// <param name="Actions">Buttons stored with the notification.</param>
/// <param name="Status">One of <see cref="NotificationStatuses"/>.</param>
/// <param name="CreatedAt">Creation time.</param>
public sealed record StoredNotification(
    Guid Id,
    Guid UserId,
    NotificationKind Kind,
    string Title,
    string Body,
    DateTimeOffset? DueAt,
    ChannelId? PreferredChannel,
    Guid? ConversationId,
    IReadOnlyList<OutboundAction>? Actions,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// Notifications table. Idempotent insert by <c>(user_id, dedup_key)</c>.
/// </summary>
public interface INotificationStore
{
    /// <summary>Inserts the notification or returns the existing row for the dedup key.</summary>
    public ValueTask<NotificationReceipt> CreateOrGetAsync(NotificationRequest request, CancellationToken ct);

    /// <summary>Loads one notification. Null when it does not exist.</summary>
    public ValueTask<StoredNotification?> FindAsync(Guid notificationId, CancellationToken ct);

    /// <summary>
    /// Sets <paramref name="status"/> and, when <paramref name="status"/> is
    /// <see cref="NotificationStatuses.Delivered"/>, the delivery channel and time.
    /// </summary>
    public ValueTask<bool> UpdateStatusAsync(
        Guid notificationId,
        string status,
        string? deliveredChannel,
        CancellationToken ct);

    /// <summary>Notifications for the user delivered at or after <paramref name="since"/>.</summary>
    public ValueTask<int> CountDeliveredSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Newest delivered notices for the user since <paramref name="since"/>, at most <paramref name="limit"/>.
    /// </summary>
    public ValueTask<IReadOnlyList<DeliveredNotificationNotice>> ListRecentDeliveredAsync(
        Guid userId, DateTimeOffset since, int limit, CancellationToken ct);

    /// <summary>
    /// Newest notices for the web log, any status, at most <paramref name="limit"/>.
    /// The row has no target reference.
    /// </summary>
    public ValueTask<IReadOnlyList<NotificationLogItem>> ListRecentAsync(
        Guid userId, int limit, CancellationToken ct);
}

/// <summary>One notice for the web log. Channel and action are labels, never a target reference.</summary>
/// <param name="Id">Notification id.</param>
/// <param name="Title">Title.</param>
/// <param name="Body">Body. Empty when the column is null.</param>
/// <param name="Status">One of <see cref="NotificationStatuses"/>.</param>
/// <param name="Channel">Channel that delivered it, when one did.</param>
/// <param name="Action">Button already used, or null.</param>
/// <param name="CreatedAt">Creation time, UTC.</param>
public sealed record NotificationLogItem(
    Guid Id,
    string Title,
    string Body,
    string Status,
    string? Channel,
    string? Action,
    DateTimeOffset CreatedAt);

/// <summary>One delivered notice for the recent-notifications prompt. The body is not logged.</summary>
/// <param name="DeliveredAt">When it was delivered, UTC.</param>
/// <param name="Channel">Channel that delivered it, when known.</param>
/// <param name="Title">Title.</param>
/// <param name="Body">Body. Empty when the column is null.</param>
/// <param name="ActionTaken">Button already used, or null.</param>
/// <param name="Trust">Content trust.</param>
public sealed record DeliveredNotificationNotice(
    DateTimeOffset DeliveredAt,
    string? Channel,
    string Title,
    string Body,
    string? ActionTaken,
    NotificationContentTrust Trust);
