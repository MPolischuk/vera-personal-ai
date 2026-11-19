using Assistant.Abstractions;

namespace Assistant.Persistence.Entities;

internal sealed class MessageEntity : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool WasInterrupted { get; set; }
    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
    public ICollection<ToolInvocationEntity> ToolInvocations { get; } = [];
    public ICollection<UsageRecordEntity> UsageRecords { get; } = [];
}
