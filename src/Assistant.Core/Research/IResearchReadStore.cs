namespace Assistant.Core.Research;

/// <summary>
/// Control rows for <c>read_source</c>. The page body is not a column.
/// <see cref="ResearchRead.RetrievedAt"/> is assigned by the store, not by the model.
/// </summary>
public interface IResearchReadStore
{
    public ValueTask<ResearchRead> AddAsync(NewResearchRead read, CancellationToken cancellationToken);

    public ValueTask<ResearchRead?> GetAsync(Guid userId, Guid readId, CancellationToken cancellationToken);
}

public sealed record ResearchBoundingBox(double Left, double Top, double Right, double Bottom);

/// <summary>
/// A claim copied from a page or from a document. Page, bbox, and document id
/// are set only when the source is a converted document.
/// </summary>
public sealed record ResearchClaim(
    string Text,
    string Fragment,
    int? Page = null,
    ResearchBoundingBox? Bbox = null,
    Guid? DocumentId = null);

public sealed record NewResearchRead(
    Guid UserId,
    Guid ConversationId,
    Guid TurnId,
    string Summary,
    IReadOnlyList<ResearchClaim> Claims,
    string SourceUrl);

public sealed record ResearchRead(
    Guid Id,
    Guid UserId,
    Guid ConversationId,
    Guid TurnId,
    string Summary,
    IReadOnlyList<ResearchClaim> Claims,
    string SourceUrl,
    DateTimeOffset RetrievedAt);
