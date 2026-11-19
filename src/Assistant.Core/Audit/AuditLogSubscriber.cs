using System.Text.Json;
using Assistant.Abstractions.Events;
using Assistant.Core.Events;

namespace Assistant.Core.Audit;

internal sealed class AuditLogSubscriber(IAuditLogStore store) :
    IDomainEventHandler<TurnStarted>,
    IDomainEventHandler<TurnCompleted>,
    IDomainEventHandler<TurnInterrupted>,
    IDomainEventHandler<ToolInvoked>,
    IDomainEventHandler<ConfirmationRequested>,
    IDomainEventHandler<ConfirmationResolved>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask HandleAsync(TurnStarted domainEvent, CancellationToken ct)
        => AppendAsync(
            domainEvent.UserId,
            "turn.started",
            "conversation",
            domainEvent.ConversationId,
            new { domainEvent.TurnId },
            ct);

    public ValueTask HandleAsync(TurnCompleted domainEvent, CancellationToken ct)
        => AppendAsync(
            domainEvent.UserId,
            "turn.completed",
            "message",
            domainEvent.MessageId,
            new
            {
                domainEvent.ConversationId,
                domainEvent.TurnId,
                domainEvent.ToolInvocations,
                DurationMs = (int)domainEvent.Duration.TotalMilliseconds,
            },
            ct);

    public ValueTask HandleAsync(TurnInterrupted domainEvent, CancellationToken ct)
        => AppendAsync(
            domainEvent.UserId,
            "turn.interrupted",
            "conversation",
            domainEvent.ConversationId,
            new
            {
                domainEvent.TurnId,
                domainEvent.MessageId,
                domainEvent.EmittedCharacters,
                DurationMs = (int)domainEvent.Duration.TotalMilliseconds,
            },
            ct);

    public ValueTask HandleAsync(ToolInvoked domainEvent, CancellationToken ct)
        => AppendAsync(
            domainEvent.Context.UserId,
            "tool.invoked",
            "conversation",
            domainEvent.Context.ConversationId,
            new
            {
                domainEvent.Context.TurnId,
                domainEvent.ToolName,
                domainEvent.ExtensionName,
                Level = domainEvent.Level.ToString(),
                Outcome = domainEvent.Outcome.ToString(),
                domainEvent.Reason,
                Succeeded = domainEvent.Result?.Status.ToString(),
                DurationMs = (int)domainEvent.Duration.TotalMilliseconds,
            },
            ct);

    public ValueTask HandleAsync(ConfirmationRequested domainEvent, CancellationToken ct)
        => AppendAsync(
            domainEvent.UserId,
            "confirmation.requested",
            "confirmation",
            domainEvent.ConfirmationId,
            new
            {
                domainEvent.ConversationId,
                domainEvent.ToolName,
                ExpiresAt = domainEvent.ExpiresAt,
            },
            ct);

    public ValueTask HandleAsync(ConfirmationResolved domainEvent, CancellationToken ct)
        => AppendAsync(
            domainEvent.UserId,
            "confirmation.resolved",
            "confirmation",
            domainEvent.ConfirmationId,
            new
            {
                domainEvent.ConversationId,
                domainEvent.ToolName,
                Outcome = domainEvent.Outcome.ToString(),
            },
            ct);

    private ValueTask AppendAsync(
        Guid userId,
        string eventType,
        string subjectType,
        Guid subjectId,
        object payload,
        CancellationToken ct)
        => store.AppendAsync(
            new AuditEntry(
                userId,
                eventType,
                subjectType,
                subjectId,
                JsonSerializer.Serialize(payload, JsonOptions)),
            ct);
}
