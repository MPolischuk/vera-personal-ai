using System.Net;
using System.Text;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Neutralizes model markdown before a rich message. Images lose their URL,
/// links become visible text, and HTML outside <c>details</c>/<c>summary</c> is escaped.
/// Fenced and inline code is copied unchanged.
/// </summary>
internal static class TelegramRichMarkdownSanitizer
{
    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return "";
        }

        var builder = new StringBuilder(markdown.Length);
        var index = 0;
        while (index < markdown.Length)
        {
            if (TryCopyFence(markdown, index, out var fence))
            {
                builder.Append(markdown, index, fence);
                index += fence;
                continue;
            }

            if (TryCopyInlineCode(markdown, index, out var code))
            {
                builder.Append(markdown, index, code);
                index += code;
                continue;
            }

            if (TrySkipThinking(markdown, index, out var thinking))
            {
                index += thinking;
                continue;
            }

            if (TryImage(markdown, index, out var imageEnd, out var alt))
            {
                builder.Append(Sanitize(alt));
                index = imageEnd;
                continue;
            }

            if (TryCopyFootnote(markdown, index, out var footnote))
            {
                builder.Append(markdown, index, footnote);
                index += footnote;
                continue;
            }

            if (TryLink(markdown, index, out var linkEnd, out var label, out var url))
            {
                builder.Append(Sanitize(label));
                builder.Append(" (");
                builder.Append(url.Replace("<", "&lt;", StringComparison.Ordinal));
                builder.Append(')');
                index = linkEnd;
                continue;
            }

            if (TryHtmlTag(markdown, index, out var tagLength, out var allowed))
            {
                var tag = markdown.Substring(index, tagLength);
                builder.Append(allowed ? tag : WebUtility.HtmlEncode(tag));
                index += tagLength;
                continue;
            }

            if (markdown[index] == '<')
            {
                builder.Append("&lt;");
                index++;
                continue;
            }

            builder.Append(markdown[index]);
            index++;
        }

        return builder.ToString();
    }

    private static bool TryCopyFence(string source, int index, out int consumed)
    {
        consumed = 0;
        if (!source.AsSpan(index).StartsWith("```"))
        {
            return false;
        }

        var contentStart = index + 3;
        if (contentStart < source.Length && source[contentStart] != '\n')
        {
            var lineEnd = source.IndexOf('\n', contentStart);
            contentStart = lineEnd < 0 ? source.Length : lineEnd + 1;
        }
        else if (contentStart < source.Length && source[contentStart] == '\n')
        {
            contentStart++;
        }

        var close = source.IndexOf("```", contentStart, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        consumed = close + 3 - index;
        return true;
    }

    private static bool TryCopyInlineCode(string source, int index, out int consumed)
    {
        consumed = 0;
        if (source[index] != '`' || source.AsSpan(index).StartsWith("```"))
        {
            return false;
        }

        var close = source.IndexOf('`', index + 1);
        if (close < 0)
        {
            return false;
        }

        consumed = close + 1 - index;
        return true;
    }

    private static bool TrySkipThinking(string source, int index, out int consumed)
    {
        consumed = 0;
        const string open = "<tg-thinking";
        if (!source.AsSpan(index).StartsWith(open, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterName = index + open.Length;
        if (afterName < source.Length && source[afterName] is not ('>' or '/' or ' ' or '\t' or '\r' or '\n'))
        {
            return false;
        }

        var openEnd = source.IndexOf('>', afterName);
        if (openEnd < 0)
        {
            return false;
        }

        const string close = "</tg-thinking>";
        var closeAt = source.IndexOf(close, openEnd + 1, StringComparison.OrdinalIgnoreCase);
        consumed = closeAt < 0 ? openEnd + 1 - index : closeAt + close.Length - index;
        return true;
    }

    private static bool TryImage(string source, int index, out int end, out string alt)
    {
        end = 0;
        alt = "";
        if (!source.AsSpan(index).StartsWith("!["))
        {
            return false;
        }

        if (!TryBracketLink(source, index + 1, out var label, out var urlEnd))
        {
            return false;
        }

        alt = label;
        end = urlEnd;
        return true;
    }

    private static bool TryCopyFootnote(string source, int index, out int consumed)
    {
        consumed = 0;
        if ((uint)(index + 2) >= (uint)source.Length || source[index] != '[' || source[index + 1] != '^')
        {
            return false;
        }

        var idStart = index + 2;
        var close = source.IndexOf(']', idStart);
        if (close < 0 || !IsFootnoteId(source.AsSpan(idStart, close - idStart)))
        {
            return false;
        }

        var atLineStart = index == 0 || source[index - 1] is '\n' or '\r';
        if (atLineStart && close + 1 < source.Length && source[close + 1] == ':')
        {
            var lineEnd = source.IndexOf('\n', close + 2);
            if (lineEnd < 0)
            {
                lineEnd = source.Length;
            }
            else if (lineEnd > close && source[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            consumed = lineEnd - index;
            return true;
        }

        consumed = close + 1 - index;
        return true;
    }

    private static bool IsFootnoteId(ReadOnlySpan<char> id)
    {
        if (id.IsEmpty)
        {
            return false;
        }

        foreach (var character in id)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryLink(string source, int index, out int end, out string label, out string url)
    {
        end = 0;
        label = "";
        url = "";
        if (source[index] != '[')
        {
            return false;
        }

        if (!TryBracketLink(source, index, out label, out var urlEnd))
        {
            return false;
        }

        var labelEnd = source.IndexOf(']', index + 1);
        url = source[(labelEnd + 2)..(urlEnd - 1)];
        end = urlEnd;
        return true;
    }

    private static bool TryBracketLink(string source, int bracket, out string label, out int end)
    {
        label = "";
        end = 0;
        if (bracket >= source.Length || source[bracket] != '[')
        {
            return false;
        }

        var labelEnd = source.IndexOf(']', bracket + 1);
        if (labelEnd < 0 || labelEnd + 1 >= source.Length || source[labelEnd + 1] != '(')
        {
            return false;
        }

        var urlEnd = source.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
        {
            return false;
        }

        label = source[(bracket + 1)..labelEnd];
        end = urlEnd + 1;
        return true;
    }

    private static bool TryHtmlTag(string source, int index, out int consumed, out bool allowed)
    {
        consumed = 0;
        allowed = false;
        if (source[index] != '<')
        {
            return false;
        }

        var cursor = index + 1;
        if (cursor < source.Length && source[cursor] == '/')
        {
            cursor++;
        }

        if (cursor >= source.Length || !char.IsAsciiLetter(source[cursor]))
        {
            return false;
        }

        var end = source.IndexOf('>', cursor);
        if (end < 0)
        {
            return false;
        }

        consumed = end + 1 - index;
        allowed = IsDetailsOrSummary(source.AsSpan(index, consumed));
        return true;
    }

    private static bool IsDetailsOrSummary(ReadOnlySpan<char> tag)
    {
        var inner = tag[1..^1].Trim();
        if (inner.Length > 0 && inner[0] == '/')
        {
            inner = inner[1..].TrimStart();
        }

        return StartsWithTag(inner, "details") || StartsWithTag(inner, "summary");
    }

    private static bool StartsWithTag(ReadOnlySpan<char> text, string name)
    {
        if (!text.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Length == name.Length || char.IsWhiteSpace(text[name.Length]);
    }
}
