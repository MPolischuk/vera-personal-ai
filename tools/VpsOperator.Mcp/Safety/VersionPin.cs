using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal static partial class VersionPin
{
    public static bool IsCoolify44(string? version) =>
        version is not null && Coolify44().IsMatch(version);

    // 4.4.7 is the first 4.4 release with PHP 8.4.27 (PHP2Shell).
    [GeneratedRegex(@"^4\.4\.([7-9]|[1-9][0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Coolify44();
}
