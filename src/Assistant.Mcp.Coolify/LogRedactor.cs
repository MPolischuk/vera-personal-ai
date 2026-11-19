using System.Text;
using System.Text.RegularExpressions;

namespace Assistant.Mcp.Coolify;

/// <summary>Drops log lines that contain a secret marker as a substring.</summary>
internal static partial class LogRedactor
{
    public static string Filter(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var first = true;
        foreach (var line in text.Split('\n'))
        {
            var normalized = line.EndsWith('\r') ? line[..^1] : line;
            if (Sensitive().IsMatch(normalized))
            {
                continue;
            }

            if (!first)
            {
                builder.Append('\n');
            }

            builder.Append(normalized);
            first = false;
        }

        return builder.ToString();
    }

    [GeneratedRegex("KEY|TOKEN|PASSWORD|SECRET", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sensitive();
}
