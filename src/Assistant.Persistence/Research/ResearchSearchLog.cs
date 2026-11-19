using Assistant.Abstractions.Research;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Research;

internal sealed class ResearchSearchLog(AssistantDbContext db) : IResearchSearchLog
{
    public ValueTask<int> CountAsync(
        Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        => new(db.ResearchSearches.CountAsync(
            row => row.UserId == userId && row.CreatedAt >= fromUtc && row.CreatedAt < toUtc,
            cancellationToken));

    public async ValueTask AppendAsync(Guid userId, CancellationToken cancellationToken)
    {
        db.ResearchSearches.Add(new ResearchSearchEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
