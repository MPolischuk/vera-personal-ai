using System.Text.RegularExpressions;

namespace VpsOperator.Mcp;

internal static partial class PostgresName
{
    public static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 63 && Identifier().IsMatch(value);

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
