namespace Assistant.Core.Personality;

/// <summary>
/// Character sheet: likes, dislikes, and quirks. Only always-quirks enter the stable prompt (ADR-0013).
/// </summary>
public sealed record CharacterCatalog(
    IReadOnlyList<CharacterEntry> Likes,
    IReadOnlyList<CharacterEntry> Dislikes,
    IReadOnlyList<QuirkEntry> Quirks)
{
    public static CharacterCatalog Empty { get; } = new([], [], []);
}

public sealed record CharacterEntry(string Id, string Line, IReadOnlyList<string> Tags);

public sealed record QuirkEntry(string Id, string Line, bool Always, IReadOnlyList<string> Tags);
