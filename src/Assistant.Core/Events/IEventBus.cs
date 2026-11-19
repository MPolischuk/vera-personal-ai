using Assistant.Abstractions.Events;

namespace Assistant.Core.Events;

public interface IEventBus
{
    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : IDomainEvent;
}

public interface IDomainEventPump
{
    public Task RunAsync(CancellationToken ct);

    public ValueTask CompleteAsync();
}
