using Assistant.Core.Skills;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Assistant.Persistence.Skills;

internal sealed class SkillProposalStore(AssistantDbContext db, TimeProvider clock) : ISkillProposalStore
{
    public async ValueTask<SkillProposal> CreateAsync(CreateSkillProposal request, CancellationToken ct)
    {
        var pending = await db.SkillProposals.AnyAsync(
            proposal => proposal.UserId == request.UserId
                && proposal.Name == request.Name
                && proposal.Status == SkillProposalStatus.Pending,
            ct);
        if (pending)
        {
            throw new SkillProposalConflictException(request.Name);
        }

        var entity = new SkillProposalEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Name = request.Name,
            Description = request.Description,
            Content = request.Content,
            Status = SkillProposalStatus.Pending,
        };

        db.SkillProposals.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DetachPendingAdds();
            throw new SkillProposalConflictException(request.Name);
        }

        return ToDomain(entity);
    }

    public async ValueTask<SkillProposal?> FindAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var entity = await db.SkillProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(proposal => proposal.Id == id && proposal.UserId == userId, ct);
        return entity is null ? null : ToDomain(entity);
    }

    public async ValueTask<IReadOnlyList<SkillProposal>> ListPendingAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.SkillProposals
            .AsNoTracking()
            .Where(proposal => proposal.UserId == userId && proposal.Status == SkillProposalStatus.Pending)
            .ToListAsync(ct);

        return rows
            .Select(ToDomain)
            .OrderBy(proposal => proposal.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<bool> TryResolveAsync(
        Guid id, Guid userId, SkillProposalStatus status, CancellationToken ct)
    {
        if (status is SkillProposalStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Resolving keeps the row pending.");
        }

        var now = clock.GetUtcNow();
        var rows = await db.SkillProposals
            .Where(proposal => proposal.Id == id
                && proposal.UserId == userId
                && proposal.Status == SkillProposalStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(proposal => proposal.Status, status)
                    .SetProperty(proposal => proposal.ResolvedAt, now),
                ct);

        return rows == 1;
    }

    private void DetachPendingAdds()
    {
        foreach (var entry in db.ChangeTracker.Entries<SkillProposalEntity>()
                     .Where(item => item.State is EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }

    private static SkillProposal ToDomain(SkillProposalEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.Name,
            entity.Description,
            entity.Content,
            entity.Status,
            entity.CreatedAt,
            entity.ResolvedAt);
}
