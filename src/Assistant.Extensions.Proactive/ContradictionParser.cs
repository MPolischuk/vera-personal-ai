using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistant.Extensions.Proactive;

/// <summary>One pair the contradiction review accepted. Both ids were in the prompt.</summary>
public sealed record ContradictionPair(Guid PreviousId, Guid SuccessorId, string Reason);

/// <summary>
/// Accepts <c>{"pairs":[{"previous_id","successor_id","reason"}]}</c>.
/// An id that was not in the prompt is dropped. An invalid document yields no pairs.
/// </summary>
public static class ContradictionParser
{
    public const int ReasonMaxLength = 120;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ArgumentsJson(Guid previousId, Guid successorId)
        => JsonSerializer.Serialize(new StoredPair(previousId, successorId), Json);

    public static bool TryReadStoredPair(string? json, out Guid previousId, out Guid successorId)
    {
        previousId = Guid.Empty;
        successorId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<StoredPair>(json, Json);
            if (parsed is null || parsed.PreviousId == Guid.Empty || parsed.SuccessorId == Guid.Empty)
            {
                return false;
            }

            previousId = parsed.PreviousId;
            successorId = parsed.SuccessorId;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ContradictionPair> Parse(string? text, IReadOnlySet<Guid> admittedIds)
    {
        ArgumentNullException.ThrowIfNull(admittedIds);
        if (!TrySlice(text, out var json))
        {
            return [];
        }

        PairDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PairDocument>(json, Json);
        }
        catch (JsonException)
        {
            return [];
        }

        if (document?.Pairs is null)
        {
            return [];
        }

        var accepted = new List<ContradictionPair>();
        var seen = new HashSet<(Guid Previous, Guid Successor)>();
        foreach (var pair in document.Pairs)
        {
            if (pair is null
                || !Guid.TryParse(pair.PreviousId, out var previousId)
                || !Guid.TryParse(pair.SuccessorId, out var successorId)
                || previousId == successorId
                || !admittedIds.Contains(previousId)
                || !admittedIds.Contains(successorId))
            {
                continue;
            }

            var reason = pair.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 1 or > ReasonMaxLength || PromptInjectionScan.HasFormatCharacter(reason))
            {
                continue;
            }

            if (!seen.Add((previousId, successorId)))
            {
                continue;
            }

            accepted.Add(new ContradictionPair(previousId, successorId, reason));
        }

        return accepted;
    }

    private static bool TrySlice(string? text, out string json)
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

    private sealed record PairDocument(
        [property: JsonPropertyName("pairs")] List<PairItem?>? Pairs);

    private sealed record PairItem(
        [property: JsonPropertyName("previous_id")] string? PreviousId,
        [property: JsonPropertyName("successor_id")] string? SuccessorId,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record StoredPair(
        [property: JsonPropertyName("previous_id")] Guid PreviousId,
        [property: JsonPropertyName("successor_id")] Guid SuccessorId);
}
