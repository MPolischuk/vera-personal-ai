using Assistant.Abstractions.Events;
using Assistant.Api.Protocol;
using Assistant.Core.Events;
using Microsoft.AspNetCore.SignalR;

namespace Assistant.Api.Realtime;

internal sealed class ClientBroadcastSubscriber(
    IHubContext<AssistantHub> hub,
    TurnConnections connections,
    TimeProvider clock) :
    IDomainEventHandler<TurnStarted>,
    IDomainEventHandler<TurnCompleted>
{
    public ValueTask HandleAsync(TurnStarted domainEvent, CancellationToken ct)
    {
        if (!connections.TryGet(domainEvent.TurnId, out var origin))
        {
            return ValueTask.CompletedTask;
        }

        var envelope = MessageEnvelope.Create(
            ProtocolMessageTypes.TurnStarted,
            domainEvent.TurnId.ToString("D"),
            new TurnStartedPayload(domainEvent.TurnId, domainEvent.ConversationId),
            clock);

        return new ValueTask(hub.Clients
            .GroupExcept(UserGroups.For(domainEvent.UserId), origin.ConnectionId)
            .SendAsync("Receive", envelope, ct));
    }

    public ValueTask HandleAsync(TurnCompleted domainEvent, CancellationToken ct)
    {
        if (!connections.TryGet(domainEvent.TurnId, out var origin))
        {
            return ValueTask.CompletedTask;
        }

        var envelope = MessageEnvelope.Create(
            ProtocolMessageTypes.TurnCompleted,
            domainEvent.TurnId.ToString("D"),
            new TurnCompletedPayload(
                domainEvent.MessageId,
                "Completed",
                (int)domainEvent.Duration.TotalMilliseconds),
            clock);

        return new ValueTask(hub.Clients
            .GroupExcept(UserGroups.For(domainEvent.UserId), origin.ConnectionId)
            .SendAsync("Receive", envelope, ct));
    }
}
