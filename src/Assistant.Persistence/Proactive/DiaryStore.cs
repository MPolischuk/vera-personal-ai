using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class DiaryStore(AssistantDbContext db) : IDiaryStore
{
    public const int ReasonMaxLength = 120;

    public async ValueTask<DiaryEntry> AppendAsync(NewDiaryEntry draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var reason = draft.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > ReasonMaxLength)
        {
            throw new ArgumentException("The diary reason must be 1 to 120 characters.", nameof(draft));
        }

        if (!DiaryOutcomes.IsKnown(draft.Outcome))
        {
            throw new ArgumentException("The diary outcome is not supported.", nameof(draft));
        }

        var entity = new DiaryEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = draft.UserId,
            ProposalId = draft.ProposalId,
            PreviousId = draft.PreviousId,
            SuccessorId = draft.SuccessorId,
            Reason = reason,
            Outcome = draft.Outcome,
        };
        db.Set<DiaryEntity>().Add(entity);
        await db.SaveChangesAsync(ct);
        string? status = null;
        if (entity.ProposalId is Guid proposalId)
        {
            status = await db.Set<ProposalEntity>()
                .Where(proposal => proposal.Id == proposalId && proposal.UserId == draft.UserId)
                .Select(proposal => proposal.Status)
                .FirstOrDefaultAsync(ct);
        }

        return ToModel(entity, status);
    }

    public async ValueTask<IReadOnlyList<DiaryEntry>> ListAsync(Guid userId, CancellationToken ct)
    {
        var entries = await db.Set<DiaryEntity>()
            .Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.CreatedAt)
            .ToListAsync(ct);
        var statuses = await StatusesAsync(userId, entries, ct);
        return entries.Select(entry => ToModel(entry, StatusOf(entry, statuses))).ToList();
    }

    public async ValueTask<DiaryEntry?> FindAsync(Guid entryId, Guid userId, CancellationToken ct)
    {
        var entity = await db.Set<DiaryEntity>()
            .FirstOrDefaultAsync(entry => entry.Id == entryId && entry.UserId == userId, ct);
        if (entity is null)
        {
            return null;
        }

        var statuses = await StatusesAsync(userId, [entity], ct);
        return ToModel(entity, StatusOf(entity, statuses));
    }

    public async ValueTask<Proposal?> FindProposalAsync(Guid proposalId, Guid userId, CancellationToken ct)
    {
        var entity = await db.Set<ProposalEntity>()
            .FirstOrDefaultAsync(proposal => proposal.Id == proposalId && proposal.UserId == userId, ct);
        return entity is null ? null : ToProposal(entity);
    }

    public async ValueTask<bool> TrySetProposalStatusAsync(
        Guid proposalId,
        Guid userId,
        string status,
        DateTimeOffset resolvedAt,
        CancellationToken ct)
    {
        if (status is not (ProposalStatuses.Approved or ProposalStatuses.Rejected))
        {
            throw new ArgumentException("The proposal status is not a resolution.", nameof(status));
        }

        var entity = await db.Set<ProposalEntity>()
            .FirstOrDefaultAsync(proposal => proposal.Id == proposalId && proposal.UserId == userId, ct);
        if (entity is null || entity.Status != ProposalStatuses.Pending)
        {
            return false;
        }

        entity.Status = status;
        entity.ResolvedAt = resolvedAt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<Dictionary<Guid, string>> StatusesAsync(
        Guid userId, IReadOnlyList<DiaryEntity> entries, CancellationToken ct)
    {
        var ids = entries
            .Where(entry => entry.ProposalId is not null)
            .Select(entry => entry.ProposalId!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await db.Set<ProposalEntity>()
            .Where(proposal => proposal.UserId == userId && ids.Contains(proposal.Id))
            .ToDictionaryAsync(proposal => proposal.Id, proposal => proposal.Status, ct);
    }

    private static string? StatusOf(DiaryEntity entry, Dictionary<Guid, string> statuses)
        => entry.ProposalId is Guid proposalId && statuses.TryGetValue(proposalId, out var status)
            ? status
            : null;

    private static DiaryEntry ToModel(DiaryEntity entity, string? proposalStatus)
        => new(
            entity.Id,
            entity.UserId,
            entity.ProposalId,
            entity.PreviousId,
            entity.SuccessorId,
            entity.Reason,
            entity.Outcome,
            entity.CreatedAt,
            proposalStatus);

    private static Proposal ToProposal(ProposalEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.Origin,
            entity.ToolName,
            entity.ArgumentsJson,
            entity.Reason,
            entity.Status,
            entity.SubjectUpdatedAt,
            entity.CreatedAt,
            entity.ResolvedAt);
}
