namespace Assistant.Abstractions.Proactive;

/// <summary>Wire values for <c>proactive_proposals.origin</c>.</summary>
public static class ProposalOrigins
{
    public const string Heartbeat = "heartbeat";

    public const string Consolidation = "consolidation";
}

/// <summary>Wire values for <c>proactive_proposals.status</c>.</summary>
public static class ProposalStatuses
{
    public const string Pending = "pending";

    public const string Approved = "approved";

    public const string Rejected = "rejected";
}

/// <summary>A write the heartbeat refused to run. Applying it belongs to a later package.</summary>
public sealed record Proposal(
    Guid Id,
    Guid UserId,
    string Origin,
    string ToolName,
    string ArgumentsJson,
    string Reason,
    string Status,
    DateTimeOffset? SubjectUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>Insert. Status is always <see cref="ProposalStatuses.Pending"/>.</summary>
public sealed record NewProposal(
    Guid UserId,
    string Origin,
    string ToolName,
    string ArgumentsJson,
    string Reason,
    DateTimeOffset? SubjectUpdatedAt);

/// <summary>Proposals left for review. This package only inserts.</summary>
public interface IProposalStore
{
    public ValueTask<Proposal> InsertAsync(NewProposal draft, DateTimeOffset now, CancellationToken ct);
}
