using Assistant.Core.Personality;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Personality;

internal sealed class PersonalityStore(AssistantDbContext db) : IPersonalityStore
{
    public async ValueTask SaveActiveAsync(
        Guid userId, string profileName, string definitionJson, CancellationToken ct)
    {
        var existing = await db.PersonalityProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Name == profileName, ct);

        if (existing is { IsActive: true }
            && string.Equals(existing.Definition, definitionJson, StringComparison.Ordinal))
        {
            return; // Nothing changed: do not write, and updated_at does not lie.
        }

        // Two saves in one transaction: in a single SaveChanges, EF Core may emit the
        // activation before deactivation and the partial unique index rejects the batch (D13).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.PersonalityProfiles
            .Where(p => p.UserId == userId && p.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false), ct);

        // ExecuteUpdateAsync bypasses the tracker: detach so memory does not reactivate.
        if (existing is not null)
        {
            db.Entry(existing).State = EntityState.Detached;
        }

        var profile = await db.PersonalityProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Name == profileName, ct);

        if (profile is null)
        {
            db.PersonalityProfiles.Add(new PersonalityProfileEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Name = profileName,
                Definition = definitionJson,
                IsActive = true,
            });
        }
        else
        {
            profile.Definition = definitionJson;
            profile.IsActive = true;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
