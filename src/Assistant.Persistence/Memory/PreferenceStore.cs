using Assistant.Core.Memory;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Memory;

internal sealed class PreferenceStore(AssistantDbContext db) : IPreferenceStore
{
    public async ValueTask<IReadOnlyList<PreferenceItem>> ListAsync(Guid userId, CancellationToken ct)
        => await db.Preferences
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Key)
            .Select(p => new PreferenceItem(
                p.Id, p.UserId, p.Key, p.Value, p.CreatedAt, p.UpdatedAt, p.WriteProvenance))
            .ToListAsync(ct);

    public async ValueTask<PreferenceItem> UpsertAsync(
        Guid userId, string key, string value, WriteProvenance writeProvenance, CancellationToken ct)
    {
        var entity = await db.Preferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, ct);

        if (entity is null)
        {
            entity = new PreferenceEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Key = key,
            };
            db.Preferences.Add(entity);
        }

        entity.Value = value;
        entity.WriteProvenance = writeProvenance;
        await db.SaveChangesAsync(ct);
        return new PreferenceItem(
            entity.Id,
            entity.UserId,
            entity.Key,
            entity.Value,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.WriteProvenance);
    }

    public async ValueTask<bool> DeleteAsync(Guid userId, string key, CancellationToken ct)
    {
        var entity = await db.Preferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, ct);
        if (entity is null)
        {
            return false;
        }

        db.Preferences.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
