using Assistant.Abstractions.Events;
using Assistant.Core.Memory;

namespace Assistant.Core.Events;

public sealed record MemoryStored(
    Guid UserId,
    Guid MemoryId,
    MemoryKind Kind,
    Guid ConversationId,
    Guid TurnId) : IDomainEvent;

public sealed record MemoryForgotten(
    Guid UserId,
    Guid MemoryId,
    Guid ConversationId,
    Guid TurnId) : IDomainEvent;

public sealed record PreferenceUpdated(
    Guid UserId,
    string Key,
    Guid ConversationId,
    Guid TurnId) : IDomainEvent;
