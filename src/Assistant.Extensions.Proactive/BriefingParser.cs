using System.Text.Json;
using Assistant.Abstractions.Proactive;

namespace Assistant.Extensions.Proactive;

/// <summary>One block item accepted by <see cref="BriefingParser"/>.</summary>
public sealed record ParsedBriefingItem(
    string Block,
    string Title,
    string Reason,
    string Triage,
    string Source,
    string? SourceRef);

/// <summary>JSON document the 08:00 generate must match. Prose is rejected.</summary>
public sealed record ParsedBriefing(
    string Summary,
    IReadOnlyList<ParsedBriefingItem> Today,
    IReadOnlyList<ParsedBriefingItem> Ahead);

/// <summary>
/// Accepts only the two blocks. Rejects a missing block, an unknown triage,
/// a reason over 120 characters, and an <c>ahead</c> item that is not an active goal.
/// </summary>
public static class BriefingParser
{
    public const int ReasonMaxLength = 120;

    public const int TitleMaxLength = 200;

    public const int SummaryMaxLength = 1000;

    public const int MaxItemsPerBlock = 30;

    public static bool TryParse(
        string? text,
        IReadOnlySet<Guid> activeGoalIds,
        out ParsedBriefing? briefing,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(activeGoalIds);
        briefing = null;
        error = null;
        if (!TrySliceJson(text, out var json))
        {
            error = "json_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "json_object";
                return false;
            }

            var root = document.RootElement;
            if (!TryString(root, "summary", SummaryMaxLength, out var summary))
            {
                error = "summary";
                return false;
            }

            if (!root.TryGetProperty("today", out var todayElement)
                || !root.TryGetProperty("ahead", out var aheadElement))
            {
                error = "missing_block";
                return false;
            }

            if (!TryBlock(todayElement, BriefingBlocks.Today, activeGoalIds, ahead: false, out var today, out error))
            {
                return false;
            }

            if (!TryBlock(aheadElement, BriefingBlocks.Ahead, activeGoalIds, ahead: true, out var ahead, out error))
            {
                return false;
            }

            briefing = new ParsedBriefing(summary, today, ahead);
            return true;
        }
        catch (JsonException)
        {
            error = "json_invalid";
            return false;
        }
    }

    private static bool TryBlock(
        JsonElement element,
        string block,
        IReadOnlySet<Guid> activeGoalIds,
        bool ahead,
        out IReadOnlyList<ParsedBriefingItem> items,
        out string? error)
    {
        items = [];
        error = null;
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = "missing_block";
            return false;
        }

        if (element.GetArrayLength() > MaxItemsPerBlock)
        {
            error = "too_many_items";
            return false;
        }

        var parsed = new List<ParsedBriefingItem>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = "item";
                return false;
            }

            if (!TryString(item, "title", TitleMaxLength, out var title))
            {
                error = "title";
                return false;
            }

            if (!TryReason(item, out var reason))
            {
                error = item.TryGetProperty("reason", out var raw)
                    && raw.ValueKind == JsonValueKind.String
                    && (raw.GetString() ?? string.Empty).Trim().Length > ReasonMaxLength
                    ? "reason_length"
                    : "reason";
                return false;
            }

            if (!TryTriage(item, out var triage))
            {
                error = "triage";
                return false;
            }

            if (!TryString(item, "source", 40, out var source))
            {
                error = "source";
                return false;
            }

            var sourceRef = ReadOptional(item, "source_ref");
            if (sourceRef is not null && sourceRef.Length > 80)
            {
                error = "source_ref";
                return false;
            }

            if (ahead)
            {
                if (!string.Equals(source, "goal", StringComparison.Ordinal))
                {
                    error = "ahead_source";
                    return false;
                }

                if (sourceRef is null || !Guid.TryParse(sourceRef, out var goalId) || !activeGoalIds.Contains(goalId))
                {
                    error = "ahead_goal";
                    return false;
                }
            }

            parsed.Add(new ParsedBriefingItem(block, title, reason, triage, source, sourceRef));
        }

        items = parsed;
        return true;
    }

    private static bool TryReason(JsonElement item, out string reason)
    {
        reason = string.Empty;
        if (!item.TryGetProperty("reason", out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = (property.GetString() ?? string.Empty).Trim();
        if (text.Length is < 1 or > ReasonMaxLength || text.Contains('\n') || text.Contains('\r'))
        {
            return false;
        }

        reason = text;
        return true;
    }

    private static bool TryTriage(JsonElement item, out string triage)
    {
        triage = string.Empty;
        if (!item.TryGetProperty("triage", out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (text is not (BriefingTriage.NeedsYou or BriefingTriage.CanWait))
        {
            return false;
        }

        triage = text;
        return true;
    }

    private static bool TryString(JsonElement item, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = (property.GetString() ?? string.Empty).Trim();
        if (text.Length < 1 || text.Length > maxLength || text.Contains('\n') || text.Contains('\r'))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static string? ReadOptional(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = property.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool TrySliceJson(string? text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        json = text[start..(end + 1)];
        return true;
    }
}
