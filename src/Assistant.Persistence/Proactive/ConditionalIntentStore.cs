using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class ConditionalIntentStore(AssistantDbContext db) : IConditionalIntentStore
{
    public async ValueTask<ConditionalIntent> InsertAsync(
        Guid userId, string eventName, string text, DateTimeOffset now, CancellationToken ct)
    {
        if (!ConditionalIntentEvents.IsAllowed(eventName))
        {
            throw new ArgumentException(
                "The event name is not in the closed list.", nameof(eventName));
        }

        var cleaned = text?.Trim() ?? string.Empty;
        if (cleaned.Length is < 1 or > ConditionalIntentLimits.TextMaxLength)
        {
            throw new ArgumentException(
                "A conditional intent must be 1 to 300 characters.", nameof(text));
        }

        var entity = new ConditionalIntentEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EventName = ConditionalIntentEvents.InboundFact,
            Text = cleaned,
            Status = ConditionalIntentStatuses.Active,
            CreatedAt = now,
        };
        db.Set<ConditionalIntentEntity>().Add(entity);
        await db.SaveChangesAsync(ct);
        return ToModel(entity);
    }

    public async ValueTask<bool> PauseAsync(Guid userId, Guid intentId, CancellationToken ct)
    {
        var affected = await db.Set<ConditionalIntentEntity>()
            .Where(intent => intent.Id == intentId
                && intent.UserId == userId
                && intent.Status == ConditionalIntentStatuses.Active)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(intent => intent.Status, ConditionalIntentStatuses.Paused),
                ct);
        return affected == 1;
    }

    public async ValueTask<IReadOnlyList<ConditionalIntent>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Set<ConditionalIntentEntity>().AsNoTracking()
            .Where(intent => intent.UserId == userId)
            .OrderBy(intent => intent.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToArray();
    }

    private static ConditionalIntent ToModel(ConditionalIntentEntity entity)
        => new(entity.Id, entity.UserId, entity.EventName, entity.Text, entity.Status, entity.CreatedAt);
}
