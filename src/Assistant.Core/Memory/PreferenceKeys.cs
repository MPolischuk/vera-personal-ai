using System.Text.RegularExpressions;

namespace Assistant.Core.Memory;

public static partial class PreferenceKeys
{
    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string key) => Pattern().IsMatch(key);
}
