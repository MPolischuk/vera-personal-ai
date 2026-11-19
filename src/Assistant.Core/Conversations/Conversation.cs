using Assistant.Abstractions;

namespace Assistant.Core.Conversations;

public sealed record Conversation(
    Guid Id,
    Guid UserId,
    string? Title,
    DateTimeOffset StartedAt,
    DateTimeOffset LastMessageAt);

public sealed record ConversationMessage(
    Guid Id,
    Guid ConversationId,
    MessageRole Role,
    string Content,
    int Sequence,
    bool WasInterrupted,
    string? Metadata,
    DateTimeOffset CreatedAt);
