namespace Assistant.Persistence.Entities;

internal sealed class NotificationDeliveryEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid NotificationId { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string TargetRefHash { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public string Result { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? ExternalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
