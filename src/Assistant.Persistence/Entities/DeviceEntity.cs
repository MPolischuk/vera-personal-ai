namespace Assistant.Persistence.Entities;

internal sealed class DeviceEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte[]? RefreshTokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
