using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal static partial class ScriptArguments
{
    public static bool IsSafe(string value) =>
        value.Length is > 0 and <= 64 && Safe().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Safe();
}
