namespace Assistant.Abstractions.Notifications;

/// <summary>Result of a notification button.</summary>
public enum NotificationActionStatus
{
    /// <summary>The action was stored.</summary>
    Applied = 1,

    /// <summary>No notification for this user.</summary>
    NotFound = 2,

    /// <summary>A button was already used on this notification.</summary>
    AlreadyActed = 3,

    /// <summary>This occurrence was snoozed the maximum number of times.</summary>
    SnoozeLimit = 4,

    /// <summary>The action id is not one this notification accepts.</summary>
    Unsupported = 5,
}

/// <summary>Outcome of <see cref="INotificationActionService.ExecuteAsync"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Channel">Preferred channel of the notification, when it was found.</param>
public readonly record struct NotificationActionResult(NotificationActionStatus Status, string? Channel);

/// <summary>
/// Runs a notification button (<c>done</c>, <c>snooze:10m</c>, <c>opened</c>).
/// Deterministic: it does not call a language model.
/// </summary>
public interface INotificationActionService
{
    /// <summary>
    /// Applies <paramref name="actionId"/> to a notification owned by <paramref name="userId"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the action was applied.</returns>
    public ValueTask<bool> TryExecuteAsync(
        Guid userId, Guid notificationId, string actionId, CancellationToken ct);

    /// <summary>Same as <see cref="TryExecuteAsync"/>, with the reason when it was not applied.</summary>
    public ValueTask<NotificationActionResult> ExecuteAsync(
        Guid userId, Guid notificationId, string actionId, CancellationToken ct);
}
