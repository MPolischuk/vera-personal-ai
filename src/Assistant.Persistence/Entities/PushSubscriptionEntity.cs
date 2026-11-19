namespace Assistant.Persistence.Entities;

internal sealed class PushSubscriptionEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid DeviceId { get; set; }

    public string EndpointHash { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string? P256dh { get; set; }

    public string? Auth { get; set; }

    public string? VapidPublicKeyHash { get; set; }

    public string? UserAgentFamily { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    public int FailureCount { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
