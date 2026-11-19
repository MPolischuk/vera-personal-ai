using System.Globalization;
using System.Text.RegularExpressions;

namespace Assistant.Core.Personality;

internal static partial class PersonalityProfileValidator
{
    /// <summary>
    /// Half the budget: a prompt that nearly fills it leaves no room for history (D11).
    /// Fixed threshold on purpose: an option invites raising it when it gets in the way.
    /// </summary>
    internal const double MaxPromptBudgetFraction = 0.5;

    /// <summary>Max length of a character entry <c>line</c> after trim (D4).</summary>
    internal const int MaxCharacterLineLength = 200;

    /// <summary>
    /// Cap for <c>instructions</c> as a share of half the token budget (D4 / RF-33).
    /// With budget 8000: half = 4000, cap = 1000 estimated tokens.
    /// </summary>
    internal const double MaxInstructionsShareOfHalf = 0.25;

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CharacterIdPattern();

    public static IReadOnlyList<string> Validate(
        PersonalityProfile profile, int estimatedTokens, int tokenBudget)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            problems.Add("Name is required.");
        }
        else if (profile.Name.Trim().Length > 60)
        {
            problems.Add("Name cannot exceed 60 characters.");
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(profile.Language, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            problems.Add($"Language '{profile.Language}' is not a valid culture.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            problems.Add($"Time zone '{profile.TimeZone}' is not a valid IANA identifier.");
        }

        for (var i = 0; i < profile.Traits.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(profile.Traits[i]))
            {
                problems.Add($"Trait at position {i + 1} is empty.");
            }
        }

        for (var i = 0; i < profile.Boundaries.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(profile.Boundaries[i]))
            {
                problems.Add($"Boundary at position {i + 1} is empty.");
            }
        }

        ValidateCharacter(profile.Character, problems);
        ValidateInstructionsBudget(profile.Instructions, tokenBudget, problems);

        var cap = (int)(tokenBudget * MaxPromptBudgetFraction);
        if (estimatedTokens > cap)
        {
            problems.Add(
                $"Estimated system prompt ({estimatedTokens} tokens) exceeds half the budget ({cap} of {tokenBudget}).");
        }

        return problems;
    }

    public static void ThrowIfInvalid(PersonalityProfile profile, int estimatedTokens, int tokenBudget)
    {
        var problems = Validate(profile, estimatedTokens, tokenBudget);
        if (problems.Count > 0)
        {
            throw new PersonalityProfileInvalidException(profile.Name, problems);
        }
    }

    private static void ValidateCharacter(CharacterCatalog catalog, List<string> problems)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        ValidateEntries(catalog.Likes, "like", requireTags: true, seen, problems);
        ValidateEntries(catalog.Dislikes, "dislike", requireTags: true, seen, problems);

        foreach (var quirk in catalog.Quirks)
        {
            ValidateEntryBasics(quirk.Id, quirk.Line, "quirk", seen, problems);
            ValidateTags(quirk.Tags, quirk.Id, "quirk", requireAtLeastOne: !quirk.Always, problems);
        }
    }

    private static void ValidateEntries(
        IReadOnlyList<CharacterEntry> entries,
        string category,
        bool requireTags,
        Dictionary<string, string> seen,
        List<string> problems)
    {
        foreach (var entry in entries)
        {
            ValidateEntryBasics(entry.Id, entry.Line, category, seen, problems);
            ValidateTags(entry.Tags, entry.Id, category, requireAtLeastOne: requireTags, problems);
        }
    }

    private static void ValidateEntryBasics(
        string id,
        string line,
        string category,
        Dictionary<string, string> seen,
        List<string> problems)
    {
        var trimmedId = id?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedId))
        {
            problems.Add($"Character {category} is missing an id.");
        }
        else if (!CharacterIdPattern().IsMatch(trimmedId))
        {
            problems.Add(
                $"Character {category} id '{trimmedId}' is invalid. Expected pattern ^[a-z][a-z0-9_]*$.");
        }
        else if (seen.TryGetValue(trimmedId, out var previous))
        {
            problems.Add(
                $"Character id '{trimmedId}' is duplicated ({previous} and {category}).");
        }
        else
        {
            seen[trimmedId] = category;
        }

        var trimmedLine = line?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedLine))
        {
            problems.Add($"Character {category} '{trimmedId}' has an empty line.");
        }
        else if (trimmedLine.Length > MaxCharacterLineLength)
        {
            problems.Add(
                $"Character {category} '{trimmedId}' line exceeds {MaxCharacterLineLength} characters.");
        }
    }

    private static void ValidateTags(
        IReadOnlyList<string> tags,
        string id,
        string category,
        bool requireAtLeastOne,
        List<string> problems)
    {
        var label = string.IsNullOrEmpty(id) ? category : $"{category} '{id}'";
        var nonEmpty = 0;

        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(tag))
            {
                problems.Add($"Character {label} has an empty tag at position {i + 1}.");
                continue;
            }

            if (tag.Any(char.IsWhiteSpace))
            {
                problems.Add($"Character {label} tag '{tag}' contains whitespace.");
            }

            nonEmpty++;
        }

        if (requireAtLeastOne && nonEmpty == 0)
        {
            problems.Add($"Character {label} requires at least one tag.");
        }
    }

    private static void ValidateInstructionsBudget(
        string? instructions, int tokenBudget, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return;
        }

        // Same formula as HeuristicTokenEstimator: (Length + 3) / 4.
        var estimated = (instructions.Length + 3) / 4;
        var cap = (int)(tokenBudget * MaxPromptBudgetFraction * MaxInstructionsShareOfHalf);
        if (estimated > cap)
        {
            problems.Add(
                $"Instructions ({estimated} tokens) exceed 25% of half the budget ({cap} of {tokenBudget}).");
        }
    }
}
