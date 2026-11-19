namespace Assistant.Abstractions.Webhooks;

/// <summary>
/// Verifies one inbound webhook format. Idempotency is not its job.
/// </summary>
public interface IInboundSignatureVerifier
{
    /// <summary>
    /// Checks the timestamp window and the signature against the raw body.
    /// </summary>
    /// <param name="request">Headers and the unparsed body.</param>
    /// <param name="secrets">HMAC keys. Already decoded. Empty fails closed.</param>
    public InboundSignatureResult Verify(InboundWebhookRequest request, IReadOnlyList<byte[]> secrets);
}

/// <summary>
/// One inbound webhook, before any parser touches the body.
/// </summary>
/// <param name="Source">Configured source name.</param>
/// <param name="Headers">Header names in lowercase.</param>
/// <param name="Body">Raw body bytes that were signed.</param>
public sealed record InboundWebhookRequest(
    string Source,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);

/// <summary>
/// Closed result of signature verification.
/// </summary>
public abstract record InboundSignatureResult
{
    private InboundSignatureResult()
    {
    }

    /// <summary>The timestamp is inside the window and a signature matched.</summary>
    /// <param name="DeliveryId">Delivery id taken from the signed headers.</param>
    /// <param name="Timestamp">Timestamp taken from the signed headers.</param>
    public sealed record Valid(string DeliveryId, DateTimeOffset Timestamp) : InboundSignatureResult;

    /// <summary>The request must be rejected.</summary>
    /// <param name="Code">Stable rejection code. Not a secret and not the body.</param>
    public sealed record Invalid(string Code) : InboundSignatureResult;
}
