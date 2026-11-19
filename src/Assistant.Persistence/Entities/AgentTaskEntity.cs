namespace Assistant.Persistence.Entities;

internal sealed class AgentTaskEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ConversationId { get; set; }

    public Guid OriginTurnId { get; set; }

    public Guid? UsageTurnId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Instruction { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    public string? PartialResult { get; set; }

    public decimal CostUsd { get; set; }

    public decimal CostCapUsd { get; set; }

    public DateTimeOffset DeadlineAt { get; set; }

    public bool Idempotent { get; set; }

    public int RestartCount { get; set; }

    public string? ExternalTaskId { get; set; }

    public bool NoticeArmed { get; set; } = true;

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
