using System.Globalization;
using System.Text;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// ASI06 scan for this step: a Unicode format character (category Cf),
/// including bidi overrides and zero-width characters. The text is not logged.
/// </summary>
public static class PromptInjectionScan
{
    public static bool HasFormatCharacter(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                return true;
            }
        }

        return false;
    }
}
