namespace Assistant.Core.Skills;

public interface ISkillProposalStore
{
    public ValueTask<SkillProposal> CreateAsync(CreateSkillProposal request, CancellationToken ct);

    public ValueTask<SkillProposal?> FindAsync(Guid id, Guid userId, CancellationToken ct);

    public ValueTask<IReadOnlyList<SkillProposal>> ListPendingAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Updates a row that is still pending and owned by <paramref name="userId"/>.
    /// Returns whether that update affected a row.
    /// </summary>
    public ValueTask<bool> TryResolveAsync(
        Guid id, Guid userId, SkillProposalStatus status, CancellationToken ct);
}

public enum SkillProposalStatus
{
    Pending,
    Approved,
    Discarded,
}

public sealed record SkillProposal(
    Guid Id,
    Guid UserId,
    string Name,
    string Description,
    string Content,
    SkillProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record CreateSkillProposal(
    Guid UserId,
    string Name,
    string Description,
    string Content);

public sealed class SkillProposalConflictException : Exception
{
    public SkillProposalConflictException(string name)
        : base($"A pending skill proposal already exists for '{name}'.")
    {
        Name = name;
    }

    public string Name { get; }
}
