using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Proactive;

internal sealed class HeartbeatChecklistStore(AssistantDbContext db) : IHeartbeatChecklistStore
{
    public const int TextMaxLength = 2000;

    public async ValueTask<string?> GetAsync(Guid userId, CancellationToken ct)
        => await db.Set<HeartbeatCheckEntity>().AsNoTracking()
            .Where(check => check.UserId == userId)
            .Select(check => check.Text)
            .SingleOrDefaultAsync(ct);

    public async ValueTask SetAsync(Guid userId, string text, CancellationToken ct)
    {
        var cleaned = text?.Trim() ?? string.Empty;
        if (cleaned.Length > TextMaxLength)
        {
            throw new ArgumentException("The checklist must be at most 2000 characters.", nameof(text));
        }

        var existing = await db.Set<HeartbeatCheckEntity>()
            .SingleOrDefaultAsync(check => check.UserId == userId, ct);
        if (existing is null)
        {
            db.Set<HeartbeatCheckEntity>().Add(new HeartbeatCheckEntity
            {
                UserId = userId,
                Text = cleaned,
            });
        }
        else
        {
            existing.Text = cleaned;
        }

        await db.SaveChangesAsync(ct);
    }
}
