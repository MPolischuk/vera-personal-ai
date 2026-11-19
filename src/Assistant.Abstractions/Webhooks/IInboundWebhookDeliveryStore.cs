namespace Assistant.Abstractions.Webhooks;

/// <summary>Limits shared by the signature check and the delivery table.</summary>
public static class InboundWebhookLimits
{
    /// <summary>Primary-key column <c>delivery_id</c>.</summary>
    public const int MaxDeliveryIdLength = 256;

    /// <summary>Primary-key column <c>source</c>.</summary>
    public const int MaxSourceLength = 64;
}

/// <summary>
/// Idempotency of inbound webhook deliveries. A repeated id never reaches the handler.
/// </summary>
public interface IInboundWebhookDeliveryStore
{
    /// <summary>
    /// Inserts the delivery when this source has not seen the id.
    /// </summary>
    /// <returns>False when the id was already recorded (replay).</returns>
    public ValueTask<bool> TryRecordAsync(
        string source,
        string deliveryId,
        DateTimeOffset receivedAt,
        CancellationToken ct);

    /// <summary>Deletes rows whose <c>received_at</c> is strictly before the cutoff.</summary>
    public ValueTask<int> PurgeAsync(DateTimeOffset receivedBefore, CancellationToken ct);
}
