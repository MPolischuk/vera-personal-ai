using System.Text;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Splits a reply into pieces of at most <see cref="MaxLength"/> characters,
/// breaking on paragraphs, then sentences, then words.
/// </summary>
internal static class TelegramTextSplitter
{
    public const int MaxLength = 4000;

    /// <summary>Bot API rich-message limit. Longer replies are split with the same rules as HTML.</summary>
    public const int RichMaxLength = 32768;

    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(350);

    public static IReadOnlyList<string> Split(string text)
        => Split(text, MaxLength);

    public static IReadOnlyList<string> Split(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        if (text.Length == 0)
        {
            return [];
        }

        if (text.Length <= maxLength)
        {
            return [text];
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var unit in Atoms(text, maxLength))
        {
            if (unit.Length > maxLength)
            {
                Flush(chunks, current);
                for (var offset = 0; offset < unit.Length; offset += maxLength)
                {
                    chunks.Add(unit.Substring(offset, Math.Min(maxLength, unit.Length - offset)));
                }

                continue;
            }

            if (current.Length > 0 && current.Length + unit.Length > maxLength)
            {
                Flush(chunks, current);
            }

            current.Append(unit);
        }

        Flush(chunks, current);
        return chunks;
    }

    private static void Flush(List<string> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        chunks.Add(current.ToString());
        current.Clear();
    }

    private static IEnumerable<string> Atoms(string text, int maxLength)
    {
        foreach (var paragraph in SplitKeep(text, "\n\n"))
        {
            if (paragraph.Length <= maxLength)
            {
                yield return paragraph;
                continue;
            }

            foreach (var sentence in Sentences(paragraph))
            {
                if (sentence.Length <= maxLength)
                {
                    yield return sentence;
                    continue;
                }

                foreach (var word in Words(sentence))
                {
                    yield return word;
                }
            }
        }
    }

    private static IEnumerable<string> Sentences(string paragraph)
    {
        var start = 0;
        for (var index = 0; index < paragraph.Length; index++)
        {
            var current = paragraph[index];
            if (current is not ('.' or '!' or '?' or '…'))
            {
                continue;
            }

            var end = index + 1;
            if (end < paragraph.Length && paragraph[end] == ' ')
            {
                end++;
            }

            yield return paragraph[start..end];
            start = end;
            index = end - 1;
        }

        if (start < paragraph.Length)
        {
            yield return paragraph[start..];
        }
    }

    private static IEnumerable<string> Words(string sentence)
    {
        var start = 0;
        for (var index = 0; index < sentence.Length; index++)
        {
            if (!char.IsWhiteSpace(sentence[index]))
            {
                continue;
            }

            var end = index;
            while (end < sentence.Length && char.IsWhiteSpace(sentence[end]))
            {
                end++;
            }

            yield return sentence[start..end];
            start = end;
            index = end - 1;
        }

        if (start < sentence.Length)
        {
            yield return sentence[start..];
        }
    }

    private static IEnumerable<string> SplitKeep(string text, string separator)
    {
        var start = 0;
        while (start <= text.Length)
        {
            var found = text.IndexOf(separator, start, StringComparison.Ordinal);
            if (found < 0)
            {
                if (start < text.Length)
                {
                    yield return text[start..];
                }

                yield break;
            }

            if (found > start)
            {
                yield return text[start..found];
            }

            yield return separator;
            start = found + separator.Length;
        }
    }
}
