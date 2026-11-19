using System.Net;
using System.Text;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Minimal Markdown to Telegram HTML. Images stay escaped text: never a link and never media.
/// </summary>
internal static class TelegramMarkdownToHtml
{
    public static string ToHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Render(markdown.Replace("\r\n", "\n", StringComparison.Ordinal), markup: true);
    }

    public static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "");

    private static string Render(string source, bool markup)
    {
        var builder = new StringBuilder(source.Length + 16);
        var index = 0;
        while (index < source.Length)
        {
            if (markup && TryConsume(source, index, out var consumed, out var html))
            {
                builder.Append(html);
                index += consumed;
                continue;
            }

            var start = index;
            while (index < source.Length && !(markup && IsSpecial(source, index)))
            {
                index++;
            }

            if (index == start)
            {
                builder.Append(Escape(source.Substring(index, 1)));
                index++;
                continue;
            }

            builder.Append(Escape(source[start..index]));
        }

        return builder.ToString();
    }

    private static bool IsSpecial(string source, int index)
    {
        var current = source[index];
        return current is '`' or '*' or '_' or '[' or '!';
    }

    private static bool TryConsume(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
        if (TryFence(source, index, out consumed, out html)
            || TryInlineCode(source, index, out consumed, out html)
            || TryImage(source, index, out consumed, out html)
            || TryLink(source, index, out consumed, out html)
            || TryWrapped(source, index, "**", "<b>", "</b>", out consumed, out html)
            || TryItalicStar(source, index, out consumed, out html)
            || TryItalicUnderscore(source, index, out consumed, out html))
        {
            return true;
        }

        consumed = 0;
        html = "";
        return false;
    }

    private static bool TryFence(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
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
        html = "<pre>" + Escape(source[contentStart..close]) + "</pre>";
        return true;
    }

    private static bool TryInlineCode(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
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
        html = "<code>" + Escape(source[(index + 1)..close]) + "</code>";
        return true;
    }

    private static bool TryImage(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
        if (!source.AsSpan(index).StartsWith("!["))
        {
            return false;
        }

        if (!TryLinkEnd(source, index + 1, out var end))
        {
            return false;
        }

        consumed = end - index;
        html = Escape(source[index..end]);
        return true;
    }

    private static bool TryLink(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
        if (source[index] != '[')
        {
            return false;
        }

        var labelEnd = source.IndexOf(']', index + 1);
        if (labelEnd < 0 || labelEnd + 1 >= source.Length || source[labelEnd + 1] != '(')
        {
            return false;
        }

        var urlEnd = source.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
        {
            return false;
        }

        var label = Render(source[(index + 1)..labelEnd], markup: true);
        var url = Escape(source[(labelEnd + 2)..urlEnd]);
        consumed = urlEnd + 1 - index;
        html = "<a href=\"" + url + "\">" + label + "</a>";
        return true;
    }

    private static bool TryLinkEnd(string source, int bracket, out int end)
    {
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

        end = urlEnd + 1;
        return true;
    }

    private static bool TryWrapped(
        string source,
        int index,
        string marker,
        string openTag,
        string closeTag,
        out int consumed,
        out string html)
    {
        consumed = 0;
        html = "";
        if (!source.AsSpan(index).StartsWith(marker))
        {
            return false;
        }

        var contentStart = index + marker.Length;
        var close = source.IndexOf(marker, contentStart, StringComparison.Ordinal);
        if (close < 0 || close == contentStart)
        {
            return false;
        }

        consumed = close + marker.Length - index;
        html = openTag + Render(source[contentStart..close], markup: true) + closeTag;
        return true;
    }

    private static bool TryItalicStar(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
        if (source[index] != '*' || (index + 1 < source.Length && source[index + 1] == '*'))
        {
            return false;
        }

        for (var cursor = index + 1; cursor < source.Length; cursor++)
        {
            if (source[cursor] != '*')
            {
                continue;
            }

            var nextIsStar = cursor + 1 < source.Length && source[cursor + 1] == '*';
            var previousIsStar = source[cursor - 1] == '*';
            if (nextIsStar || previousIsStar || cursor == index + 1)
            {
                continue;
            }

            consumed = cursor + 1 - index;
            html = "<i>" + Render(source[(index + 1)..cursor], markup: true) + "</i>";
            return true;
        }

        return false;
    }

    private static bool TryItalicUnderscore(string source, int index, out int consumed, out string html)
    {
        consumed = 0;
        html = "";
        if (source[index] != '_' || !IsBoundary(source, index))
        {
            return false;
        }

        for (var cursor = index + 1; cursor < source.Length; cursor++)
        {
            if (source[cursor] != '_' || cursor == index + 1 || !IsBoundary(source, cursor))
            {
                continue;
            }

            consumed = cursor + 1 - index;
            html = "<i>" + Render(source[(index + 1)..cursor], markup: true) + "</i>";
            return true;
        }

        return false;
    }

    private static bool IsBoundary(string source, int index)
    {
        var before = index == 0 || char.IsWhiteSpace(source[index - 1]) || char.IsPunctuation(source[index - 1]);
        var after = index + 1 >= source.Length
            || char.IsWhiteSpace(source[index + 1])
            || char.IsPunctuation(source[index + 1]);
        return before || after;
    }
}
