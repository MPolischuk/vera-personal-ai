using Assistant.Core.Research;

namespace Assistant.Core.Notes;

/// <summary>
/// Knowledge notes copied from a research read. A note is not a memory.
/// <see cref="NoteSource.RetrievedAt"/> is the read's date, assigned by that store.
/// </summary>
public interface INoteStore
{
    public ValueTask<Note> CreateAsync(NewNote note, CancellationToken cancellationToken);

    public ValueTask<Note?> GetAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);

    public ValueTask<IReadOnlyList<ScoredNote>> SearchAsync(
        SearchNotesQuery query, CancellationToken cancellationToken);

    public ValueTask<Note?> UpdateAsync(NoteUpdate update, CancellationToken cancellationToken);

    public ValueTask<NoteLinkStatus> LinkAsync(
        Guid userId, Guid noteId, Guid otherNoteId, CancellationToken cancellationToken);

    public ValueTask<bool> SoftDeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken);

    public ValueTask<IReadOnlyList<Note>> ListActiveAsync(Guid userId, CancellationToken cancellationToken);
}

public enum NoteLinkStatus
{
    Linked,
    NotFound,
    SameNote,
}

public sealed record NewNote(
    Guid UserId,
    string Title,
    string Body,
    float[] Embedding,
    IReadOnlyList<NoteSource> Sources);

/// <summary>
/// One claim copied from a read. Url, or document id plus page and bbox, not both.
/// </summary>
public sealed record NoteSource(
    string ClaimText,
    string Fragment,
    DateTimeOffset RetrievedAt,
    string? Url,
    Guid? DocumentId,
    int? Page,
    ResearchBoundingBox? Bbox);

public sealed record Note(
    Guid Id,
    Guid UserId,
    string Title,
    string Body,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NoteSource> Sources,
    IReadOnlyList<NoteNeighbor> Neighbors);

public sealed record NoteNeighbor(Guid Id, string Title);

public sealed record ScoredNote(Note Note, float Score);

public sealed record SearchNotesQuery(Guid UserId, float[] Embedding, int TopK);

/// <summary>
/// A null title, body, or embedding leaves that field unchanged.
/// <see cref="Verified"/> is the only way <see cref="Note.VerifiedAt"/> moves.
/// </summary>
public sealed record NoteUpdate(
    Guid UserId,
    Guid NoteId,
    string? Title,
    string? Body,
    float[]? Embedding,
    bool Verified);
