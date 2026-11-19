namespace Assistant.Persistence.Entities;

internal sealed class NotificationEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string DedupKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string Trust { get; set; } = string.Empty;

    public DateTimeOffset? DueAt { get; set; }

    public string? PreferredChannel { get; set; }

    public Guid? ConversationId { get; set; }

    public string? Actions { get; set; }

    public string? SourceType { get; set; }

    public string? SourceId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? DeliveredChannel { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? ActedAt { get; set; }

    public string? ActionTaken { get; set; }
}
