namespace Assistant.Api.Webhooks;

/// <summary>
/// <c>whsec_</c> secrets: the prefix plus standard base64 of 24 to 64 key bytes.
/// </summary>
internal static class WebhookSecrets
{
    public const string Prefix = "whsec_";

    public const int MinBytes = 24;

    public const int MaxBytes = 64;

    public static bool TryDecode(string secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrEmpty(secret) || !secret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = secret[Prefix.Length..];
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length is < MinBytes or > MaxBytes)
        {
            return false;
        }

        key = decoded;
        return true;
    }
}
