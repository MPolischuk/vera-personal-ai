namespace Assistant.Core.Memory;

public interface IMemoryStore
{
    public ValueTask<MemoryItem> UpsertAsync(UpsertMemoryRequest request, CancellationToken ct);

    public ValueTask<MemoryItem?> FindAsync(Guid memoryId, Guid userId, CancellationToken ct);

    public ValueTask ForgetAsync(Guid memoryId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Nulls content, index terms, and the embedding of forgotten rows whose
    /// <c>deleted_at</c> is at or before <paramref name="cutoff"/>. The id stays.
    /// A replacement (<c>superseded_by</c> and no <c>deleted_at</c>) is left intact.
    /// </summary>
    public ValueTask<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken ct);

    /// <summary>
    /// Clears <c>deleted_at</c> when the row was forgotten after <paramref name="cutoff"/>
    /// and the text is still stored. <see cref="MemoryRestoreStatus.Expired"/> once the
    /// grace has passed or the text is gone.
    /// </summary>
    public ValueTask<MemoryRestoreStatus> RestoreAsync(
        Guid memoryId, Guid userId, DateTimeOffset cutoff, CancellationToken ct);

    public ValueTask<IReadOnlyList<ScoredMemory>> SearchAsync(SearchMemoryRequest request, CancellationToken ct);

    public ValueTask<IReadOnlyList<MemoryItem>> ListAsync(Guid userId, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Current, replaced, and forgotten rows for <paramref name="userId"/>.
    /// The user predicate is applied before the global delete filter is ignored.
    /// A tombstone has null content. The embedding is not included.
    /// </summary>
    public ValueTask<MemoryJourney> ListJourneyAsync(Guid userId, CancellationToken ct);

    public ValueTask<IReadOnlyList<MemoryItem>> ListPinnedAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Active rows for <paramref name="userId"/> whose evidence ids include <paramref name="evidenceId"/>.
    /// </summary>
    public ValueTask<IReadOnlyList<MemoryItem>> ListCitingAsync(
        Guid userId, Guid evidenceId, CancellationToken ct);

    /// <summary>Users that still have at least one active memory.</summary>
    public ValueTask<IReadOnlyList<Guid>> ListUserIdsAsync(CancellationToken ct);

    /// <summary>
    /// Closes <paramref name="replacesId"/> at the current instant, points <c>superseded_by</c>
    /// at the inserted row, and leaves the previous text in place. Does not set <c>deleted_at</c>.
    /// Returns null when the id is missing, deleted, or belongs to another user.
    /// </summary>
    public ValueTask<MemoryItem?> ReplaceAsync(
        Guid replacesId, UpsertMemoryRequest request, CancellationToken ct);

    /// <summary>
    /// Closes <paramref name="previousId"/> at <paramref name="at"/> and points
    /// <c>superseded_by</c> at <paramref name="successorId"/>. Does not change the text,
    /// the embedding, or <c>deleted_at</c>. <see cref="MemorySupersedeStatus.VersionMismatch"/>
    /// when <c>updated_at</c> is not <paramref name="expectedUpdatedAt"/> at microsecond resolution.
    /// </summary>
    public ValueTask<MemorySupersedeStatus> SupersedeAsync(
        Guid previousId,
        Guid successorId,
        Guid userId,
        DateTimeOffset expectedUpdatedAt,
        DateTimeOffset at,
        CancellationToken ct);

    /// <summary>
    /// Reopens <paramref name="previousId"/> and clears <c>superseded_by</c> only when it
    /// still points at <paramref name="successorId"/> and the row is not deleted.
    /// </summary>
    public ValueTask<bool> ClearSupersessionAsync(
        Guid previousId, Guid userId, Guid successorId, CancellationToken ct);

    public ValueTask<bool> SetPinnedAsync(Guid memoryId, Guid userId, bool pinned, CancellationToken ct);
}

public enum MemorySupersedeStatus
{
    Done,
    Missing,
    VersionMismatch,
}

public sealed record UpsertMemoryRequest(
    Guid? Id,
    Guid UserId,
    MemoryKind Kind,
    string Content,
    Guid? SourceConversationId,
    DateTimeOffset? DueAt,
    float[] Embedding,
    string EmbeddingModel,
    WriteProvenance WriteProvenance,
    bool Pinned = false,
    string? IndexTerms = null,
    IReadOnlyList<Guid>? DerivedFrom = null);

public sealed record SearchMemoryRequest(
    Guid UserId,
    float[] Query,
    int TopK,
    float MinScore,
    MemoryKind? Kind,
    DateTimeOffset? Since = null,
    bool ExcludePinned = false,
    bool IncludeHistory = false);

public sealed record ScoredMemory(MemoryItem Item, float Score);

public enum MemoryRestoreStatus
{
    Restored,
    NotFound,
    Expired,
}

public sealed record MemoryJourney(
    IReadOnlyList<MemoryJourneyItem> Current,
    IReadOnlyList<MemoryJourneyItem> Replaced,
    IReadOnlyList<MemoryJourneyItem> Forgotten);

public sealed record MemoryJourneyItem(
    Guid Id,
    Guid UserId,
    MemoryKind Kind,
    string? Content,
    DateTimeOffset CreatedAt,
    WriteProvenance WriteProvenance,
    bool Pinned,
    string? IndexTerms,
    Guid? SupersededBy);
