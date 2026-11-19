namespace Assistant.Persistence.Entities;

internal sealed class ChannelLinkTokenEntity : IHasCreatedAt
{
    public string TokenHash { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public string Channel { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
