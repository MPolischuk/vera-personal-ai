using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class StandingOrderStore(AssistantDbContext db) : IStandingOrderStore
{
    public async ValueTask<StandingOrder> InsertAsync(
        Guid userId, string text, DateTimeOffset now, CancellationToken ct)
    {
        var cleaned = text?.Trim() ?? string.Empty;
        if (cleaned.Length is < 1 or > StandingOrderLimits.TextMaxLength)
        {
            throw new ArgumentException(
                "A standing order must be 1 to 500 characters.", nameof(text));
        }

        var entity = new StandingOrderEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Text = cleaned,
            Status = StandingOrderStatuses.Active,
            CreatedAt = now,
        };
        db.Set<StandingOrderEntity>().Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    public async ValueTask<bool> PauseAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var affected = await db.Set<StandingOrderEntity>()
            .Where(order => order.Id == orderId
                && order.UserId == userId
                && order.Status == StandingOrderStatuses.Active)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(order => order.Status, StandingOrderStatuses.Paused),
                ct);
        return affected == 1;
    }

    public async ValueTask<IReadOnlyList<StandingOrder>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Set<StandingOrderEntity>().AsNoTracking()
            .Where(order => order.UserId == userId)
            .OrderBy(order => order.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToArray();
    }

    private static StandingOrder ToModel(StandingOrderEntity entity)
        => new(entity.Id, entity.UserId, entity.Text, entity.Status, entity.CreatedAt);
}
