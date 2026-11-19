namespace Assistant.Abstractions.Push;

/// <summary>How a push-service response changes one subscription row.</summary>
public enum PushDeliveryEffect
{
    /// <summary>The push service accepted the message (201).</summary>
    Succeeded,

    /// <summary>The attempt failed and the subscription stays active.</summary>
    Failed,

    /// <summary>VAPID was rejected (401/403). The subscription becomes <c>disabled</c>.</summary>
    Disabled,

    /// <summary>The subscription is gone (404/410) or the device unsubscribed. Status becomes <c>revoked</c>.</summary>
    Revoked,
}

/// <summary>Active subscription the sender can post to. The endpoint is a send capability.</summary>
/// <param name="Id">Subscription id.</param>
/// <param name="Endpoint">Push service URL. Never log it.</param>
/// <param name="P256dh">Client P-256 public key, base64url. Never log it.</param>
/// <param name="Auth">Client auth secret, base64url. Never log it.</param>
public sealed record PushSubscriptionTarget(Guid Id, string Endpoint, string P256dh, string Auth);

/// <summary>Subscription row safe to return from the API. No endpoint and no client keys.</summary>
/// <param name="Id">Subscription id.</param>
/// <param name="UserAgentFamily">Coarse client family, for the settings screen.</param>
/// <param name="Status"><c>active</c>, <c>disabled</c>, or <c>revoked</c>.</param>
/// <param name="CreatedAt">When the row was created.</param>
/// <param name="LastSuccessAt">Last accepted delivery.</param>
/// <param name="LastFailureAt">Last rejected delivery.</param>
public sealed record PushSubscriptionSummary(
    Guid Id,
    string? UserAgentFamily,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt);

/// <summary>
/// Push subscriptions for one user. The store lives in Persistence; the outbound channel and the
/// subscription endpoints depend on this port.
/// </summary>
public interface IPushSubscriptionReader
{
    /// <summary>Whether the user has at least one <c>active</c> subscription.</summary>
    public ValueTask<bool> HasActiveAsync(Guid userId, CancellationToken ct);

    /// <summary>Active subscriptions of the user that have client keys.</summary>
    public ValueTask<IReadOnlyList<PushSubscriptionTarget>> ListActiveTargetsAsync(Guid userId, CancellationToken ct);

    /// <summary>Active subscriptions of one device that have client keys.</summary>
    public ValueTask<IReadOnlyList<PushSubscriptionTarget>> ListActiveTargetsByDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken ct);

    /// <summary>Subscriptions of the user that are not <c>revoked</c>, without the endpoint.</summary>
    public ValueTask<IReadOnlyList<PushSubscriptionSummary>> ListSummariesAsync(Guid userId, CancellationToken ct);

    /// <summary>Inserts or updates the row keyed by the endpoint hash and marks it <c>active</c>.</summary>
    public ValueTask<Guid> UpsertAsync(
        Guid userId,
        Guid deviceId,
        string endpoint,
        string? p256dh,
        string? auth,
        string? vapidPublicKeyHash,
        string? userAgentFamily,
        CancellationToken ct);

    /// <summary>Marks every non-revoked subscription of this device as <c>revoked</c>.</summary>
    /// <returns>Whether any row changed.</returns>
    public ValueTask<bool> RevokeCurrentAsync(Guid userId, Guid deviceId, CancellationToken ct);

    /// <summary>Applies a push-service outcome to one subscription.</summary>
    public ValueTask RecordDeliveryAsync(Guid subscriptionId, PushDeliveryEffect effect, CancellationToken ct);
}
