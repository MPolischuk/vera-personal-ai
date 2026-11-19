using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Assistant.Persistence.Proactive;

internal sealed class BriefingStore(AssistantDbContext db) : IBriefingStore
{
    public async ValueTask<Briefing?> FindByLocalDateAsync(Guid userId, DateOnly localDate, CancellationToken ct)
    {
        var entity = await Query()
            .SingleOrDefaultAsync(briefing => briefing.UserId == userId && briefing.LocalDate == localDate, ct);
        return entity is null ? null : ToModel(entity);
    }

    public async ValueTask<Briefing?> FindAsync(Guid userId, Guid briefingId, CancellationToken ct)
    {
        var entity = await Query()
            .SingleOrDefaultAsync(briefing => briefing.Id == briefingId && briefing.UserId == userId, ct);
        return entity is null ? null : ToModel(entity);
    }

    public async ValueTask<BriefingSave> SaveAsync(NewBriefing draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var existing = await FindByLocalDateAsync(draft.UserId, draft.LocalDate, ct);
        if (existing is not null)
        {
            return new BriefingSave(existing, false);
        }

        var entity = new BriefingEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = draft.UserId,
            LocalDate = draft.LocalDate,
            Summary = draft.Summary,
        };
        foreach (var item in draft.Items)
        {
            entity.Items.Add(new BriefingItemEntity
            {
                Id = Guid.CreateVersion7(),
                BriefingId = entity.Id,
                Block = item.Block,
                Title = item.Title,
                Reason = item.Reason,
                Triage = item.Triage,
                Source = item.Source,
                SourceRef = item.SourceRef,
            });
        }

        db.Briefings.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.Entry(entity).State = EntityState.Detached;
            foreach (var item in entity.Items)
            {
                db.Entry(item).State = EntityState.Detached;
            }

            var winner = await FindByLocalDateAsync(draft.UserId, draft.LocalDate, ct);
            if (winner is null)
            {
                throw;
            }

            return new BriefingSave(winner, false);
        }

        return new BriefingSave(ToModel(entity), true);
    }

    public async ValueTask<bool> SetFeedbackAsync(
        Guid userId,
        Guid briefingId,
        Guid itemId,
        string feedback,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!BriefingFeedback.IsKnown(feedback))
        {
            throw new ArgumentException("The feedback value is not supported.", nameof(feedback));
        }

        var owned = await db.Briefings.AsNoTracking()
            .AnyAsync(briefing => briefing.Id == briefingId && briefing.UserId == userId, ct);
        if (!owned)
        {
            return false;
        }

        var affected = await db.BriefingItems
            .Where(item => item.Id == itemId && item.BriefingId == briefingId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Feedback, feedback)
                    .SetProperty(item => item.FeedbackAt, now),
                ct);
        return affected == 1;
    }

    private IQueryable<BriefingEntity> Query()
        => db.Briefings.AsNoTracking().Include(briefing => briefing.Items);

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static Briefing ToModel(BriefingEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.LocalDate,
            entity.Summary,
            entity.CreatedAt,
            entity.Items
                .OrderBy(item => item.Block, StringComparer.Ordinal)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
                .Select(item => new BriefingItem(
                    item.Id,
                    item.BriefingId,
                    item.Block,
                    item.Title,
                    item.Reason,
                    item.Triage,
                    item.Source,
                    item.SourceRef,
                    item.Feedback,
                    item.FeedbackAt))
                .ToArray());
}
