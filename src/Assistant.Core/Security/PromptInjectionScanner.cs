using System.Globalization;
using System.Text;

namespace Assistant.Core.Security;

internal static class PromptInjectionScanner
{
    internal const string RejectionMessage =
        "No guardé eso: el texto tiene una instrucción incrustada o caracteres invisibles.";

    private static readonly string[] Phrases =
    [
        "ignore previous",
        "ignore all previous",
        "disregard previous",
        "ignore the above",
        "system prompt",
        "you are now",
        "olvida las instrucciones",
        "ignora las instrucciones",
        "olvida todo lo anterior",
    ];

    public static PromptInjectionInspection Inspect(string text)
    {
        // Format and disallowed controls reject first, so a zero-width split still fails.
        if (HasForbiddenCharacter(text))
        {
            return PromptInjectionInspection.Rejected;
        }

        foreach (var phrase in Phrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return PromptInjectionInspection.Rejected;
            }
        }

        return PromptInjectionInspection.Clean;
    }

    private static bool HasForbiddenCharacter(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Format)
            {
                return true;
            }

            if (category == UnicodeCategory.Control
                && rune.Value is not ('\n' or '\r' or '\t'))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly record struct PromptInjectionInspection(bool IsRejected)
{
    public static PromptInjectionInspection Clean { get; } = new(false);

    public static PromptInjectionInspection Rejected { get; } = new(true);
}
