using Assistant.Core.Audit;
using Assistant.Persistence.Entities;

namespace Assistant.Persistence.Audit;

internal sealed class AuditLogStore(AssistantDbContext db) : IAuditLogStore
{
    public async ValueTask AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = entry.UserId,
            EventType = entry.EventType,
            SubjectType = entry.SubjectType,
            SubjectId = entry.SubjectId,
            Payload = entry.PayloadJson,
        });

        await db.SaveChangesAsync(ct);
    }
}
