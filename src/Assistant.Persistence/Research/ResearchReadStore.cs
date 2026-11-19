using System.Text.Json;
using Assistant.Core.Research;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Research;

internal sealed class ResearchReadStore(AssistantDbContext db, TimeProvider clock) : IResearchReadStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<ResearchRead> AddAsync(NewResearchRead read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(read.Claims);

        var entity = new ResearchReadEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = read.UserId,
            ConversationId = read.ConversationId,
            TurnId = read.TurnId,
            Summary = read.Summary,
            Claims = JsonSerializer.Serialize(read.Claims, Json),
            SourceUrl = read.SourceUrl,
            RetrievedAt = clock.GetUtcNow(),
        };

        db.ResearchReads.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDomain(entity, read.Claims);
    }

    public async ValueTask<ResearchRead?> GetAsync(Guid userId, Guid readId, CancellationToken cancellationToken)
    {
        var entity = await db.ResearchReads
            .FirstOrDefaultAsync(row => row.Id == readId && row.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        var claims = JsonSerializer.Deserialize<List<ResearchClaim>>(entity.Claims, Json)
            ?? throw new InvalidOperationException("The stored claims are invalid.");

        return ToDomain(entity, claims);
    }

    private static ResearchRead ToDomain(ResearchReadEntity entity, IReadOnlyList<ResearchClaim> claims)
        => new(
            entity.Id,
            entity.UserId,
            entity.ConversationId,
            entity.TurnId,
            entity.Summary,
            claims,
            entity.SourceUrl,
            entity.RetrievedAt);
}
