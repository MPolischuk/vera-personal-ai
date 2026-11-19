namespace Assistant.Core.Research;

/// <summary>
/// Turns uploaded bytes into chunks. Implementations post the bytes; they never send a URL.
/// </summary>
public interface IDocumentConverter
{
    public ValueTask<IReadOnlyList<DocumentChunk>> ConvertAsync(
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken cancellationToken);
}

public sealed record DocumentChunk(
    string Text,
    int Page,
    ResearchBoundingBox Bbox,
    IReadOnlyList<string> Headings);

/// <summary>
/// Files for one user, under <c>{userId}/{id}</c>. An id from another user does not open.
/// </summary>
public interface IDocumentLibrary
{
    public ValueTask<Guid> SaveAsync(
        Guid userId,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken);

    public ValueTask<StoredDocument?> OpenAsync(
        Guid userId,
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed record StoredDocument(Guid Id, string MediaType, byte[] Content);
