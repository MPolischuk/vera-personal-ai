namespace Assistant.Abstractions.Notifications;

/// <summary>Stored result values of <c>notification_deliveries.result</c>.</summary>
public static class DeliveryResults
{
    public const string Pending = "pending";

    public const string Delivered = "delivered";

    public const string AwaitingAck = "awaiting_ack";

    public const string Acked = "acked";

    public const string AckTimeout = "ack_timeout";

    public const string Transient = "transient";

    public const string Permanent = "permanent";

    public const string NotApplicable = "not_applicable";
}

/// <summary>One delivery attempt. <see cref="TargetRefHash"/> is never the raw target.</summary>
/// <param name="Id">Delivery id. This is also <c>OutboundMessage.MessageId</c>.</param>
/// <param name="NotificationId">Parent notification.</param>
/// <param name="Channel">Channel id value.</param>
/// <param name="TargetRefHash">Truncated SHA-256 of the target reference.</param>
/// <param name="Attempt">1-based attempt number for the notification.</param>
/// <param name="Result">One of <see cref="DeliveryResults"/>.</param>
/// <param name="ErrorCode">Provider or router code, when there is one.</param>
/// <param name="ExternalId">Provider message id, when there is one.</param>
public sealed record StoredDelivery(
    Guid Id,
    Guid NotificationId,
    string Channel,
    string TargetRefHash,
    int Attempt,
    string Result,
    string? ErrorCode,
    string? ExternalId);

/// <summary>One row per delivery attempt. The id is chosen by the router before send.</summary>
public interface INotificationDeliveryStore
{
    /// <summary>Inserts an attempt with result <see cref="DeliveryResults.Pending"/>.</summary>
    public ValueTask BeginAsync(
        Guid deliveryId,
        Guid notificationId,
        string channel,
        string targetRefHash,
        int attempt,
        CancellationToken ct);

    /// <summary>Closes an attempt.</summary>
    public ValueTask CompleteAsync(
        Guid deliveryId,
        string result,
        string? errorCode,
        string? externalId,
        CancellationToken ct);

    /// <summary>How many attempts exist for the notification.</summary>
    public ValueTask<int> CountByNotificationAsync(Guid notificationId, CancellationToken ct);

    /// <summary>Attempts for the notification, oldest first.</summary>
    public ValueTask<IReadOnlyList<StoredDelivery>> ListAsync(Guid notificationId, CancellationToken ct);
}
