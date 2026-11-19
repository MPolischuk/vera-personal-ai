namespace Assistant.Persistence.Entities;

internal sealed class ChannelPeerEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string ExternalPeerId { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Username { get; set; }

    public string? PairingCodeHash { get; set; }

    public DateTimeOffset? PairingCodeExpiresAt { get; set; }

    public int PairingAttempts { get; set; }

    public DateTimeOffset? LastCodeReminderAt { get; set; }

    public Guid? ConversationId { get; set; }

    public bool DeliveryBlocked { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public Guid? ApprovedByDeviceId { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastInboundAt { get; set; }
}
