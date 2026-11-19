namespace Assistant.Persistence.Entities;

internal sealed class ConversationEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public UserEntity User { get; set; } = null!;
    public ICollection<MessageEntity> Messages { get; } = [];
    public ICollection<UsageRecordEntity> UsageRecords { get; } = [];
    public ICollection<PendingConfirmationEntity> PendingConfirmations { get; } = [];
}
