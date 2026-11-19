namespace Assistant.Persistence.Entities;

internal sealed class ChannelInboxEntity
{
    public long Id { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public string ExternalPeerId { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Payload { get; set; }

    public string Status { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
