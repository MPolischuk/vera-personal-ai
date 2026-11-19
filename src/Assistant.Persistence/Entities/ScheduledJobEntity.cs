namespace Assistant.Persistence.Entities;

internal sealed class ScheduledJobEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public string? IdempotencyKey { get; set; }

    public DateTimeOffset RunAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
