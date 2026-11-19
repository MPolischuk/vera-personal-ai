using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class ProposalStore(AssistantDbContext db) : IProposalStore
{
    public const int ToolNameMaxLength = 128;

    public const int ReasonMaxLength = 500;

    public async ValueTask<Proposal> InsertAsync(NewProposal draft, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Origin is not (ProposalOrigins.Heartbeat or ProposalOrigins.Consolidation))
        {
            throw new ArgumentException("The proposal origin is not supported.", nameof(draft));
        }

        var toolName = draft.ToolName?.Trim() ?? string.Empty;
        if (draft.Origin == ProposalOrigins.Consolidation)
        {
            if (toolName.Length > ToolNameMaxLength)
            {
                throw new ArgumentException("The tool name must be at most 128 characters.", nameof(draft));
            }

            toolName = string.Empty;
        }
        else if (toolName.Length is < 1 or > ToolNameMaxLength)
        {
            throw new ArgumentException("The tool name must be 1 to 128 characters.", nameof(draft));
        }

        var reason = draft.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > ReasonMaxLength)
        {
            throw new ArgumentException("The proposal reason must be 1 to 500 characters.", nameof(draft));
        }

        var arguments = string.IsNullOrWhiteSpace(draft.ArgumentsJson) ? "{}" : draft.ArgumentsJson;
        var entity = new ProposalEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = draft.UserId,
            Origin = draft.Origin,
            ToolName = toolName,
            ArgumentsJson = arguments,
            Reason = reason,
            Status = ProposalStatuses.Pending,
            SubjectUpdatedAt = draft.SubjectUpdatedAt,
            CreatedAt = now,
        };
        db.Set<ProposalEntity>().Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    private static Proposal ToModel(ProposalEntity entity)
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
