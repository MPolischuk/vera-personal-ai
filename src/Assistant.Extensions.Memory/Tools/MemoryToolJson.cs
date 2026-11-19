using System.Text.Json;
using Assistant.Core.Memory;

namespace Assistant.Extensions.Memory.Tools;

internal static class MemoryToolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Remember(MemoryItem item, bool updated)
        => JsonSerializer.Serialize(
            new { id = item.Id, updated, kind = Kind(item.Kind) },
            Options);

    public static string Forgotten(Guid id)
        => JsonSerializer.Serialize(new { id, forgotten = true }, Options);

    public static string Hits(IReadOnlyList<ScoredMemory> hits)
        => hits.Count == 0
            ? EmptyHits
            : JsonSerializer.Serialize(
                hits.Select(hit => new
                {
                    id = hit.Item.Id,
                    kind = Kind(hit.Item.Kind),
                    content = hit.Item.Content,
                    score = hit.Score,
                }),
                Options);

    public const string EmptyHits = "[]";

    public static string Preferences(IReadOnlyList<PreferenceItem> items)
        => items.Count == 0
            ? EmptyPreferences
            : JsonSerializer.Serialize(
                items.Select(item => new { key = item.Key, value = item.Value }),
                Options);

    public const string EmptyPreferences = "[]";

    public static string Preference(PreferenceItem item)
        => JsonSerializer.Serialize(new { key = item.Key, value = item.Value }, Options);

    private static string Kind(MemoryKind kind) => kind.ToString().ToLowerInvariant();
}
