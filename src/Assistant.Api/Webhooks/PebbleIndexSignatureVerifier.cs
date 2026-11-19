using System.Globalization;
using System.Text;
using Assistant.Abstractions.Webhooks;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Pebble Index 01 version 1. <c>X-Index-Signature</c> is lowercase hex HMAC-SHA256 with no prefix.
/// The key is the decoded <c>whsec_</c> secret. The signed bytes are the version prefix, the
/// timestamp, the delivery id, the trigger, the test flag, and then the raw body.
/// </summary>
internal sealed class PebbleIndexSignatureVerifier(TimeProvider clock) : IInboundSignatureVerifier
{
    public const string Format = "pebble-index";

    public InboundSignatureResult Verify(InboundWebhookRequest request, IReadOnlyList<byte[]> secrets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!request.Headers.TryGetValue("x-index-webhook-version", out var version)
            || !request.Headers.TryGetValue("x-index-timestamp", out var timestampText)
            || !request.Headers.TryGetValue("x-index-delivery", out var deliveryId)
            || !request.Headers.TryGetValue("x-index-trigger", out var trigger)
            || !request.Headers.TryGetValue("x-index-signature", out var signature)
            || string.IsNullOrEmpty(deliveryId)
            || deliveryId.Length > InboundWebhookLimits.MaxDeliveryIdLength
            || string.IsNullOrEmpty(trigger))
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.SignatureInvalid);
        }

        if (!TryParseTimestamp(timestampText, out var timestamp))
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.TimestampOutOfRange);
        }

        // The leading "v1" authenticates X-Index-Webhook-Version. Anything else is not this contract.
        if (!string.Equals(version, "1", StringComparison.Ordinal))
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.SignatureInvalid);
        }

        var testFlag = request.Headers.TryGetValue("x-index-test", out var testHeader)
            && string.Equals(testHeader, "true", StringComparison.Ordinal)
            ? "1"
            : "0";
        var prefix = Encoding.UTF8.GetBytes($"v{version}\n{timestampText}\n{deliveryId}\n{trigger}\n{testFlag}\n");
        var content = new byte[prefix.Length + request.Body.Length];
        prefix.CopyTo(content, 0);
        request.Body.Span.CopyTo(content.AsSpan(prefix.Length));

        var candidates = DecodeHex(signature);
        if (!HmacSha256.AnyMatch(secrets, content, candidates))
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.SignatureInvalid);
        }

        return new InboundSignatureResult.Valid(deliveryId, timestamp);
    }

    private bool TryParseTimestamp(string text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var unix))
        {
            return false;
        }

        try
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var delta = (clock.GetUtcNow() - timestamp).Duration();
        return delta <= StandardWebhooksVerifier.Tolerance;
    }

    private static List<byte[]> DecodeHex(string signature)
    {
        var candidates = new List<byte[]>(capacity: 1);
        try
        {
            var decoded = Convert.FromHexString(signature.Trim());
            if (decoded.Length == HmacSha256.Length)
            {
                candidates.Add(decoded);
            }
        }
        catch (FormatException)
        {
            // Not hex, or an odd length: this signature does not match.
        }

        return candidates;
    }
}
