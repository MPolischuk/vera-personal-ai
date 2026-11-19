namespace Assistant.Abstractions.Context;

public sealed record ContextRequest(
    Guid ConversationId,
    Guid UserId,
    Guid TurnId,
    string UserMessage,
    IReadOnlyList<ContextMessage> RecentHistory,
    DateTimeOffset Now);

public sealed record ContextMessage(MessageRole Role, string Content);
