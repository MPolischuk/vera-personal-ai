namespace Assistant.Core.Audit;

public interface IAuditLogStore
{
    public ValueTask AppendAsync(AuditEntry entry, CancellationToken ct);
}

public sealed record AuditEntry(
    Guid UserId,
    string EventType,
    string? SubjectType,
    Guid? SubjectId,
    string? PayloadJson);
