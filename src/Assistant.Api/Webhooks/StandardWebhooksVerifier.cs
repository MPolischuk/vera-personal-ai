using System.Globalization;
using System.Text;
using Assistant.Abstractions.Webhooks;

namespace Assistant.Api.Webhooks;

/// <summary>
/// Standard Webhooks symmetric signatures (<c>v1</c>). Other prefixes, including <c>v1a</c>, are ignored.
/// The signed bytes are <c>{id}.{timestamp}.{raw body}</c>.
/// </summary>
internal sealed class StandardWebhooksVerifier(TimeProvider clock) : IInboundSignatureVerifier
{
    public const string Format = "standard";

    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(300);

    public InboundSignatureResult Verify(InboundWebhookRequest request, IReadOnlyList<byte[]> secrets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!request.Headers.TryGetValue("webhook-id", out var id)
            || !request.Headers.TryGetValue("webhook-timestamp", out var timestampText)
            || !request.Headers.TryGetValue("webhook-signature", out var signatureHeader)
            || string.IsNullOrEmpty(id)
            || id.Length > InboundWebhookLimits.MaxDeliveryIdLength)
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.SignatureInvalid);
        }

        if (!TryParseTimestamp(timestampText, out var timestamp))
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.TimestampOutOfRange);
        }

        var candidates = DecodeV1(signatureHeader);
        var content = SignedContent(id, timestampText, request.Body.Span);
        if (!HmacSha256.AnyMatch(secrets, content, candidates))
        {
            return new InboundSignatureResult.Invalid(InboundWebhookCodes.SignatureInvalid);
        }

        return new InboundSignatureResult.Valid(id, timestamp);
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
        return delta <= Tolerance;
    }

    private static List<byte[]> DecodeV1(string header)
    {
        var candidates = new List<byte[]>();
        foreach (var token in header.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith("v1,", StringComparison.Ordinal))
            {
                continue;
            }

            var encoded = token["v1,".Length..];
            try
            {
                var decoded = Convert.FromBase64String(encoded);
                if (decoded.Length == HmacSha256.Length)
                {
                    candidates.Add(decoded);
                }
            }
            catch (FormatException)
            {
                // A broken token does not match. Other v1 tokens can still match.
            }
        }

        return candidates;
    }

    private static byte[] SignedContent(string id, string timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{id}.{timestamp}.");
        var content = new byte[prefix.Length + body.Length];
        prefix.CopyTo(content, 0);
        body.CopyTo(content.AsSpan(prefix.Length));
        return content;
    }
}
