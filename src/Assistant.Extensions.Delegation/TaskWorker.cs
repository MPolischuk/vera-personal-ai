using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Delegation;

/// <summary>
/// Reaps expired leases, claims <c>submitted</c> tasks, and runs one worker call per claim.
/// </summary>
internal sealed partial class TaskWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptionsMonitor<DelegationOptions> options,
    AgentTaskRuns runs,
    ILogger<TaskWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Running> _running = [];
    private readonly string _workerId = NewWorkerId();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PulseAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PulseFailed(logger, exception.GetType().Name);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(PulseInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task PulseAsync(CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAgentTaskStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            await store.ReapExpiredAsync(clock.GetUtcNow(), ct);
            await AgentTaskNoticePublisher.PublishArmedAsync(store, publisher, clock.GetUtcNow(), CancellationToken.None);
        }

        if (!options.CurrentValue.Enabled)
        {
            await CancelLocalAsync(ct);
            return;
        }

        var settings = options.CurrentValue;
        while (!ct.IsCancellationRequested)
        {
            int free;
            lock (_gate)
            {
                free = settings.MaxConcurrent - _running.Count;
            }

            if (free <= 0)
            {
                break;
            }

            ClaimedAgentTask? claimed;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IAgentTaskStore>();
                claimed = await store.ClaimAsync(_workerId, clock.GetUtcNow(), settings.Lease, ct);
            }

            if (claimed is null)
            {
                break;
            }

            Start(claimed);
        }
    }

    internal async Task WhenIdleAsync(CancellationToken ct)
    {
        while (true)
        {
            Task[] pending;
            lock (_gate)
            {
                pending = _running.Values.Select(run => run.Work).ToArray();
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending).WaitAsync(ct);
            await Task.Yield();
        }
    }

    private void Start(ClaimedAgentTask task)
    {
        var cancel = new CancellationTokenSource();
        var work = RunAsync(task, cancel);
        lock (_gate)
        {
            _running[task.Id] = new Running(task.UserId, cancel, work);
        }

        runs.Register(task.Id, cancel);
        _ = work.ContinueWith(
            finished =>
            {
                _ = finished.Exception;
                lock (_gate)
                {
                    _running.Remove(task.Id);
                }

                runs.Forget(task.Id);
                cancel.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private async Task RunAsync(ClaimedAgentTask task, CancellationTokenSource run)
    {
        using var renewStop = new CancellationTokenSource();
        var renew = Task.CompletedTask;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentTaskStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();

            if (task.CostUsd >= task.CostCapUsd)
            {
                DraftHandoff.Drop(task.Id);
                await FinishAsync(store, publisher, task.Id, AgentTaskStatuses.Failed, AgentTaskFailureReasons.CostCap);
                return;
            }

            var agent = scope.ServiceProvider.GetServices<IAgentWorker>()
                .FirstOrDefault(worker => worker.Kind == task.Kind);
            if (agent is null)
            {
                DraftHandoff.Drop(task.Id);
                await FinishAsync(store, publisher, task.Id, AgentTaskStatuses.Failed, AgentTaskFailureReasons.UnknownKind);
                return;
            }

            using var deadline = new CancellationTokenSource();
            var remaining = task.DeadlineAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                deadline.Cancel();
            }
            else
            {
                deadline.CancelAfter(remaining);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.Token, deadline.Token);
            var leaseLost = 0;
            var settings = options.CurrentValue;
            renew = RenewLoopAsync(task.Id, settings, () =>
            {
                Interlocked.Exchange(ref leaseLost, 1);
                try
                {
                    run.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }, renewStop.Token);

            AgentWorkerResult result;
            try
            {
                result = await AgentRouter.RunAsync(agent, task, _workerId, linked.Token);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref leaseLost) != 0 || run.IsCancellationRequested)
            {
                DraftHandoff.Drop(task.Id);
                return;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                DraftHandoff.Drop(task.Id);
                await FinishAsync(store, publisher, task.Id, AgentTaskStatuses.Failed, AgentTaskFailureReasons.Deadline);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                DraftHandoff.Drop(task.Id);
                var reason = MapFault(scope.ServiceProvider.GetRequiredService<IProviderFaultClassifier>().Classify(exception));
                TaskEnded(logger, task.Id, reason, exception.GetType().Name);
                await FinishAsync(store, publisher, task.Id, AgentTaskStatuses.Failed, reason);
                return;
            }

            if (deadline.IsCancellationRequested && !run.IsCancellationRequested)
            {
                DraftHandoff.Drop(task.Id);
                await FinishAsync(store, publisher, task.Id, AgentTaskStatuses.Failed, AgentTaskFailureReasons.Deadline);
                return;
            }

            var added = 0m;
            if (result.InputTokens is not null || result.OutputTokens is not null)
            {
                var quote = scope.ServiceProvider.GetRequiredService<IModelCostQuote>();
                added = quote.Quote(result.Model ?? "", result.InputTokens ?? 0, result.OutputTokens ?? 0);
                await store.AddCostAsync(task.Id, _workerId, added, clock.GetUtcNow(), CancellationToken.None);
            }

            var costNow = task.CostUsd + added;
            await FinishAsync(
                store,
                publisher,
                task.Id,
                AgentTaskStatuses.Completed,
                null,
                async () =>
                {
                    var speaker = scope.ServiceProvider.GetService<TaskResultSpeaker>();
                    if (speaker is null)
                    {
                        return null;
                    }

                    var phrase = await speaker.TrySpeakAsync(task, costNow, CancellationToken.None);
                    return phrase is null ? null : TaskResultSpeaker.Notice(task, phrase);
                });
        }
        finally
        {
            await renewStop.CancelAsync();
            try
            {
                await renew;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task FinishAsync(
        IAgentTaskStore store,
        INotificationPublisher publisher,
        Guid id,
        string state,
        string? reason,
        Func<Task<NotificationRequest?>>? replace = null)
    {
        var wrote = await store.CompleteAsync(
            id,
            _workerId,
            new AgentTaskClose(state, reason),
            clock.GetUtcNow(),
            CancellationToken.None);
        if (!wrote)
        {
            return;
        }

        NotificationRequest? replacement = null;
        if (replace is not null)
        {
            try
            {
                replacement = await replace();
            }
            catch (Exception exception)
            {
                PhraseFailed(logger, id, exception.GetType().Name);
            }
        }

        if (replacement is not null)
        {
            await publisher.PublishAsync(replacement, CancellationToken.None);
            await store.DisarmNoticeAsync(id, clock.GetUtcNow(), CancellationToken.None);
        }

        await AgentTaskNoticePublisher.PublishArmedAsync(store, publisher, clock.GetUtcNow(), CancellationToken.None);
    }

    private async Task CancelLocalAsync(CancellationToken ct)
    {
        (Guid Id, Guid UserId, CancellationTokenSource Cancel)[] snapshot;
        lock (_gate)
        {
            snapshot = _running
                .Select(pair => (pair.Key, pair.Value.UserId, pair.Value.Cancel))
                .ToArray();
        }

        foreach (var item in snapshot)
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentTaskStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            var notice = await store.CancelAsync(item.Id, item.UserId, clock.GetUtcNow(), ct);
            try
            {
                item.Cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (notice is not null)
            {
                await AgentTaskNoticePublisher.PublishArmedAsync(
                    store,
                    publisher,
                    clock.GetUtcNow(),
                    CancellationToken.None);
            }
        }
    }

    private async Task RenewLoopAsync(
        Guid taskId,
        DelegationOptions settings,
        Action onLost,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(settings.RenewEvery, clock, ct);
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IAgentTaskStore>();
                var renewed = await store.RenewLeaseAsync(
                    taskId,
                    _workerId,
                    clock.GetUtcNow(),
                    settings.Lease,
                    ct);
                if (!renewed)
                {
                    onLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static string MapFault(ProviderFaultClass fault)
        => fault switch
        {
            ProviderFaultClass.Budget => AgentTaskFailureReasons.ProviderBudget,
            ProviderFaultClass.Network => AgentTaskFailureReasons.Network,
            _ => AgentTaskFailureReasons.WorkerError,
        };

    private static string NewWorkerId()
    {
        var name = Environment.MachineName;
        if (name.Length > 80)
        {
            name = name[..80];
        }

        var id = $"{name}:{Guid.NewGuid():N}";
        return id.Length <= 128 ? id : id[..128];
    }

    [LoggerMessage(EventId = 780, Level = LogLevel.Error, Message = "Task pulse failed ({ExceptionType})")]
    private static partial void PulseFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 781, Level = LogLevel.Warning, Message = "Task {TaskId} ended as {Reason} after {ExceptionType}")]
    private static partial void TaskEnded(ILogger logger, Guid taskId, string reason, string exceptionType);

    [LoggerMessage(EventId = 782, Level = LogLevel.Warning, Message = "Task {TaskId} phrase failed ({ExceptionType})")]
    private static partial void PhraseFailed(ILogger logger, Guid taskId, string exceptionType);

    private sealed record Running(Guid UserId, CancellationTokenSource Cancel, Task Work);
}
