using Assistant.Abstractions.Events;

namespace Assistant.Core.Events;

public sealed record TurnStarted(Guid ConversationId, Guid UserId, Guid TurnId) : IDomainEvent;

public sealed record TurnCompleted(
    Guid ConversationId,
    Guid UserId,
    Guid TurnId,
    Guid MessageId,
    int ToolInvocations,
    TimeSpan Duration) : IDomainEvent;

public sealed record TurnInterrupted(
    Guid ConversationId,
    Guid UserId,
    Guid TurnId,
    Guid? MessageId,
    int EmittedCharacters,
    TimeSpan Duration) : IDomainEvent;
