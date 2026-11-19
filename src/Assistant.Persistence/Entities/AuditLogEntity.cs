namespace Assistant.Persistence.Entities;

internal sealed class AuditLogEntity : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public UserEntity User { get; set; } = null!;
}
