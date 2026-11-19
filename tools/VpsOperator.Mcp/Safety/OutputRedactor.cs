using System.Text;
using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal static partial class OutputRedactor
{
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var scrubbed = SanctumToken().Replace(text, "[redacted]");
        scrubbed = Bearer().Replace(scrubbed, "Bearer [redacted]");
        scrubbed = Email().Replace(scrubbed, "[redacted]");

        var builder = new StringBuilder(scrubbed.Length);
        var first = true;
        foreach (var line in scrubbed.Split('\n'))
        {
            var normalized = line.EndsWith('\r') ? line[..^1] : line;
            var safe = PrivateKey().IsMatch(normalized) || Assignment().IsMatch(normalized)
                ? "[redacted]"
                : normalized;
            if (!first)
            {
                builder.Append('\n');
            }

            builder.Append(safe);
            first = false;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\b\d+\|[A-Za-z0-9]{20,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SanctumToken();

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex("PRIVATE KEY", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(
        @"(?i)(^|\s)([A-Z0-9_]*(KEY|TOKEN|PASSWORD|SECRET)[A-Z0-9_]*|BACKUP_S3_[A-Z0-9_]+)\s*=",
        RegexOptions.CultureInvariant)]
    private static partial Regex Assignment();
}
