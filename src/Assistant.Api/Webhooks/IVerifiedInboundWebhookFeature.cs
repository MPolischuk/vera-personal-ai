namespace Assistant.Api.Webhooks;

/// <summary>
/// Present only after the signature, the timestamp, and the delivery id were accepted.
/// Endpoints under <c>/api/hooks</c> must require it.
/// </summary>
public interface IVerifiedInboundWebhookFeature
{
    /// <summary>Configured source name.</summary>
    public string Source { get; }

    /// <summary>Delivery id that was recorded for idempotency.</summary>
    public string DeliveryId { get; }

    /// <summary>Timestamp taken from the signed headers.</summary>
    public DateTimeOffset Timestamp { get; }
}

internal sealed record VerifiedInboundWebhookFeature(
    string Source,
    string DeliveryId,
    DateTimeOffset Timestamp) : IVerifiedInboundWebhookFeature;
