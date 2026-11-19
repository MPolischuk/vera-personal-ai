using System.Threading.Channels;
using Assistant.Abstractions.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Events;

internal sealed class ChannelEventBus(
    IServiceScopeFactory scopes, ILogger<ChannelEventBus> log) : IEventBus, IDomainEventPump
{
    // Bounded with wait: if full, the turn stalls and it is visible. Silently dropping
    // events would lose audit rows that cannot be recovered (D13).
    private readonly Channel<Envelope> _channel = Channel.CreateBounded<Envelope>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        var envelope = new Envelope<TEvent>(domainEvent);

        // The normal path does not wait and therefore does not observe cancellation:
        // a terminal event (TurnInterrupted) must not be lost to the token that just canceled.
        return _channel.Writer.TryWrite(envelope)
            ? ValueTask.CompletedTask
            : _channel.Writer.WriteAsync(envelope, ct);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(ct))
        {
            // One scope per event: lets the PT-07 audit subscriber use a scoped DbContext.
            using var scope = scopes.CreateScope();
            await envelope.DeliverAsync(scope.ServiceProvider, log, ct);
        }
    }

    public ValueTask CompleteAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private abstract class Envelope
    {
        public abstract ValueTask DeliverAsync(
            IServiceProvider services, ILogger logger, CancellationToken ct);
    }

    private sealed class Envelope<TEvent>(TEvent domainEvent) : Envelope where TEvent : IDomainEvent
    {
        public override async ValueTask DeliverAsync(
            IServiceProvider services, ILogger logger, CancellationToken ct)
        {
            foreach (var subscriber in services.GetServices<IDomainEventHandler<TEvent>>())
            {
                try
                {
                    await subscriber.HandleAsync(domainEvent, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Design is explicit: a failing subscriber does not take down the turn.
                    CoreLog.SubscriberFailed(
                        logger, e, subscriber.GetType().Name, typeof(TEvent).Name);
                }
            }
        }
    }
}
