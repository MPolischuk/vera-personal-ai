using Assistant.Core.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory;

/// <summary>
/// Nulls the text and the vector of memories forgotten longer than <see cref="GracePeriod"/>.
/// One process, outside the turn. Does not take a table lock and does not run VACUUM.
/// </summary>
public sealed class MemoryPurgeService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<MemoryPurgeService> logger) : BackgroundService
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(48);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                MemoryPurgeLog.Failed(logger, exception);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task PurgeOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMemoryStore>();
        var cutoff = clock.GetUtcNow() - GracePeriod;
        var purged = await store.PurgeExpiredAsync(cutoff, ct);
        if (purged > 0)
        {
            MemoryPurgeLog.Purged(logger, purged);
        }
    }
}

internal static partial class MemoryPurgeLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Memory purge failed")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Purged {Count} forgotten memories")]
    public static partial void Purged(ILogger logger, int count);
}
