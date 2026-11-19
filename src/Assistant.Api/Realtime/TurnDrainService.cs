namespace Assistant.Api.Realtime;

internal sealed class TurnDrainService(TurnRunner runner) : IHostedService
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(20);

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => runner.DrainAsync(GracePeriod, cancellationToken);
}
