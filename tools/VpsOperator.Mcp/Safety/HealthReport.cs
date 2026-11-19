using System.Text;
using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal static partial class HealthReport
{
    public static bool IsHealthy(string? status, string? health) =>
        string.Equals(status, "running", StringComparison.Ordinal)
        && string.Equals(health, "healthy", StringComparison.Ordinal);

    public static string? FindMigration(string? log)
    {
        if (string.IsNullOrEmpty(log))
        {
            return null;
        }

        var match = MigrationName().Match(log);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool MentionsMigration(string? log) =>
        !string.IsNullOrEmpty(log) && MigrationSignal().IsMatch(log);

    public static string Excerpt(string? log)
    {
        if (string.IsNullOrEmpty(log))
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var line in log.Split('\n'))
        {
            var normalized = line.EndsWith('\r') ? line[..^1] : line;
            if (SqlStatement().IsMatch(normalized))
            {
                continue;
            }

            kept.Add(normalized);
        }

        var start = Math.Max(0, kept.Count - 30);
        var builder = new StringBuilder();
        for (var i = start; i < kept.Count; i++)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(kept[i]);
            if (builder.Length >= 1500)
            {
                break;
            }
        }

        return builder.Length <= 1500 ? builder.ToString() : builder.ToString(0, 1500);
    }

    [GeneratedRegex(@"\b(\d{14}_[A-Za-z][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationName();

    [GeneratedRegex(
        "pending migration|__efmigrationshistory|InvalidOperationException",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MigrationSignal();

    [GeneratedRegex(@"\b(ALTER|INSERT|UPDATE|DELETE|DROP|TRUNCATE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlStatement();
}
