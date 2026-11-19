namespace Assistant.Persistence.Entities;

internal sealed class PendingConfirmationEntity : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedOutcome { get; set; }
    public bool TurnContaminated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
}
