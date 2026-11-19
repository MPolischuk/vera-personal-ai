using Assistant.Core.Events;

namespace Assistant.Api.Realtime;

internal sealed class DomainEventPumpService(IDomainEventPump pump) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await pump.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await pump.CompleteAsync();
        await base.StopAsync(cancellationToken);
    }
}
