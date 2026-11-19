namespace SttBakeoff;

internal static class SecretRedaction
{
    public static string Redact(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var redacted = text;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 6)
            {
                continue;
            }

            redacted = redacted.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return redacted;
    }

    public static string Snippet(string? text, int maxChars, params string?[] secrets)
    {
        var redacted = Redact(text, secrets).Replace('\r', ' ').Replace('\n', ' ');
        if (redacted.Length <= maxChars)
        {
            return redacted;
        }

        return redacted[..maxChars];
    }
}
