using System.Globalization;
using System.Text;

namespace Assistant.Core.Personality;

/// <summary>
/// Pure tag matcher for situational character entries. No I/O, no LLM.
/// </summary>
internal static class CharacterTagMatcher
{
    /// <summary>
    /// Fixed thresholds on purpose: an option invites raising them when they get in the way
    /// (same spirit as D11).
    /// </summary>
    public const int MaxLikes = 3;

    public const int MaxDislikes = 2;

    public const int MaxSituationalQuirks = 2;

    public const int RecentUserMessages = 2;

    public const int MaxSituationalTokens = 256;

    public sealed record Selection(
        IReadOnlyList<CharacterEntry> Likes,
        IReadOnlyList<CharacterEntry> Dislikes,
        IReadOnlyList<QuirkEntry> Quirks)
    {
        public IReadOnlyList<string> Ids { get; } =
            Likes.Select(e => e.Id)
                .Concat(Dislikes.Select(e => e.Id))
                .Concat(Quirks.Select(e => e.Id))
                .ToArray();

        public bool IsEmpty => Likes.Count == 0 && Dislikes.Count == 0 && Quirks.Count == 0;
    }

    public static HashSet<string> Tokenize(string text)
    {
        var normalized = Normalize(text);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;

        for (var i = 0; i <= normalized.Length; i++)
        {
            var isLetter = i < normalized.Length && char.IsLetter(normalized[i]);
            if (isLetter)
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                tokens.Add(normalized[start..i]);
                start = -1;
            }
        }

        return tokens;
    }

    public static Selection Select(CharacterCatalog catalog, IReadOnlySet<string> tokens)
    {
        var likes = SelectTop(
            catalog.Likes,
            tokens,
            MaxLikes,
            static e => e.Tags);

        var dislikes = SelectTop(
            catalog.Dislikes,
            tokens,
            MaxDislikes,
            static e => e.Tags);

        var quirks = SelectTop(
            catalog.Quirks.Where(q => !q.Always).ToArray(),
            tokens,
            MaxSituationalQuirks,
            static e => e.Tags);

        return new Selection(likes, dislikes, quirks);
    }

    private static T[] SelectTop<T>(
        IReadOnlyList<T> entries,
        IReadOnlySet<string> tokens,
        int max,
        Func<T, IReadOnlyList<string>> tagsOf)
    {
        var scored = new List<(T Entry, int Score, int Index)>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            var score = Score(tagsOf(entries[i]), tokens);
            if (score > 0)
            {
                scored.Add((entries[i], score, i));
            }
        }

        // Top-K by score desc, file index asc; emit in file order.
        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Take(max)
            .OrderBy(x => x.Index)
            .Select(x => x.Entry)
            .ToArray();
    }

    private static int Score(IReadOnlyList<string> tags, IReadOnlySet<string> tokens)
    {
        var score = 0;
        foreach (var tag in tags)
        {
            var tagTokens = Tokenize(tag);
            if (tagTokens.Count == 1 && tokens.Contains(tagTokens.First()))
            {
                score++;
            }
        }

        return score;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var formD = text.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(formD.Length);

        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                buffer.Append(ch);
            }
        }

        return buffer.ToString().ToLowerInvariant();
    }
}
