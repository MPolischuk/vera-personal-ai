using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class GoalStore(AssistantDbContext db) : IGoalStore
{
    public const int TextMaxLength = 200;

    public async ValueTask<Goal> InsertAsync(Guid userId, string text, DateTimeOffset now, CancellationToken ct)
    {
        var cleaned = text?.Trim() ?? string.Empty;
        if (cleaned.Length is < 1 or > TextMaxLength)
        {
            throw new ArgumentException("A goal must be 1 to 200 characters.", nameof(text));
        }

        var entity = new GoalEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Text = cleaned,
            Status = GoalStatuses.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Goals.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    public async ValueTask<bool> CompleteAsync(Guid userId, Guid goalId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await db.Goals
            .Where(goal => goal.Id == goalId && goal.UserId == userId && goal.Status == GoalStatuses.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(goal => goal.Status, GoalStatuses.Done)
                    .SetProperty(goal => goal.UpdatedAt, now),
                ct);
        return affected == 1;
    }

    public async ValueTask<IReadOnlyList<Goal>> ListActiveAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Goals.AsNoTracking()
            .Where(goal => goal.UserId == userId && goal.Status == GoalStatuses.Active)
            .OrderBy(goal => goal.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToArray();
    }

    private static Goal ToModel(GoalEntity entity)
        => new(entity.Id, entity.UserId, entity.Text, entity.Status, entity.CreatedAt, entity.UpdatedAt);
}
