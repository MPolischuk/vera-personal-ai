using Assistant.Abstractions.Events;
using Assistant.Core.Confirmations;

namespace Assistant.Core.Events;

public sealed record ConfirmationRequested(
    Guid ConfirmationId,
    Guid ConversationId,
    Guid UserId,
    string ToolName,
    string Prompt,
    DateTimeOffset ExpiresAt) : IDomainEvent;

public sealed record ConfirmationResolved(
    Guid ConfirmationId,
    Guid ConversationId,
    Guid UserId,
    string ToolName,
    ConfirmationResolutionOutcome Outcome) : IDomainEvent;
