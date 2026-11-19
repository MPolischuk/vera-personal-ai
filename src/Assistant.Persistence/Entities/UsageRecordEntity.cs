namespace Assistant.Persistence.Entities;

internal sealed class UsageRecordEntity : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string TaskKind { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int TtsCharacters { get; set; }
    public double SttSeconds { get; set; }
    public int EmbeddingTokens { get; set; }
    public int CachedInputTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
    public MessageEntity? Message { get; set; }
}
