namespace Assistant.Core.Memory;

public sealed record MemoryItem(
    Guid Id,
    Guid UserId,
    MemoryKind Kind,
    string Content,
    Guid? SourceConversationId,
    DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    WriteProvenance WriteProvenance,
    bool Pinned = false,
    string? IndexTerms = null,
    IReadOnlyList<Guid>? DerivedFrom = null,
    DateTimeOffset? ValidUntil = null,
    Guid? SupersededBy = null);
