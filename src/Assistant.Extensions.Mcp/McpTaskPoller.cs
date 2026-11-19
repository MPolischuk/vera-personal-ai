using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;

namespace Assistant.Extensions.Mcp;

/// <summary>
/// Polls MCP task handles outside the turn. The turn only receives the handle.
/// </summary>
internal sealed partial class McpTaskPoller(ILogger<McpTaskPoller> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(10);

    private readonly Channel<PendingTask> _pending = Channel.CreateUnbounded<PendingTask>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, string> _terminal = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _inflight = new(StringComparer.Ordinal);
    private string? _lastPollError;

    internal string? LastPollError => _lastPollError;

    internal void Enqueue(McpClient client, CreateTaskResult created)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(created);
        _pending.Writer.TryWrite(new PendingTask(client, created.TaskId, created.PollIntervalMs));
    }

    internal bool TryGetStatus(string taskId, out string status)
        => _terminal.TryGetValue(taskId, out status!);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var pending in _pending.Reader.ReadAllAsync(stoppingToken))
            {
                _inflight[pending.TaskId] = PollAsync(pending, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollAsync(PendingTask pending, CancellationToken stoppingToken)
    {
        var interval = ClampInterval(pending.PollIntervalMs);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stoppingToken);
                var state = await pending.Client.GetTaskAsync(pending.TaskId, stoppingToken);
                interval = ClampInterval(state.PollIntervalMs ?? (long)interval.TotalMilliseconds);
                if (TerminalStatus(state.Status) is { } status)
                {
                    _terminal[pending.TaskId] = status;
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _lastPollError = ex.Message;
            PollFailed(logger, ex, pending.TaskId);
        }
        finally
        {
            _inflight.TryRemove(pending.TaskId, out _);
        }
    }

    private static TimeSpan ClampInterval(long? pollIntervalMs)
    {
        if (pollIntervalMs is not { } milliseconds || milliseconds <= MinimumInterval.TotalMilliseconds)
        {
            return MinimumInterval;
        }

        if (milliseconds >= MaximumInterval.TotalMilliseconds)
        {
            return MaximumInterval;
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static string? TerminalStatus(McpTaskStatus status) => status switch
    {
        McpTaskStatus.Completed => "completed",
        McpTaskStatus.Failed => "failed",
        McpTaskStatus.Cancelled => "cancelled",
        _ => null,
    };

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "MCP task {TaskId} poll failed.")]
    private static partial void PollFailed(ILogger logger, Exception exception, string taskId);

    private readonly record struct PendingTask(McpClient Client, string TaskId, long? PollIntervalMs);
}
