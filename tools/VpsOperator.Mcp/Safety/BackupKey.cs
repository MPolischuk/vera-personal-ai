using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal static partial class BackupKey
{
    public static bool IsAssistantDaily(string? key) =>
        key is not null && Daily().IsMatch(key);

    public static bool IsInstanceDump(string? path) =>
        path is not null && Dump().IsMatch(path);

    [GeneratedRegex(@"^daily/assistant-[0-9]{8}-[0-9]{6}\.sql\.age$", RegexOptions.CultureInvariant)]
    private static partial Regex Daily();

    [GeneratedRegex(
        @"^/home/operator/coolify-instance-backups/coolify-[0-9]{8}-[0-9]{6}\.dump$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Dump();
}
