using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory;

/// <summary>
/// Sweeps memory outside the conversation turn: after five stored memories for a user,
/// or at local midnight in America/Argentina/Buenos_Aires.
/// </summary>
public sealed class MemoryConsolidationService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<MemoryConsolidationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = ResolveSchedule();
        while (!stoppingToken.IsCancellationRequested)
        {
            var due = schedule.TryConsume(clock.GetUtcNow());
            if (due is not null)
            {
                await SweepAsync(due, stoppingToken);
            }

            try
            {
                await schedule.WaitAsync(
                    schedule.UntilNextLocalMidnight(clock.GetUtcNow()),
                    clock,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private MemoryConsolidationSchedule ResolveSchedule()
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MemoryConsolidationSchedule>();
    }

    private async Task SweepAsync(MemoryConsolidationDue due, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var consolidator = scope.ServiceProvider.GetRequiredService<MemoryConsolidator>();
            await consolidator.RunAsync(due, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            MemoryLog.ConsolidationFailed(logger, exception);
        }
    }
}
