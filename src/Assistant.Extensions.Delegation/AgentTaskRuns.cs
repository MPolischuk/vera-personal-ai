namespace Assistant.Extensions.Delegation;

/// <summary>Cancellation sources for tasks running in this process. Signaling does not wait.</summary>
internal sealed class AgentTaskRuns
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _runs = [];

    public void Register(Guid id, CancellationTokenSource cancel)
    {
        lock (_gate)
        {
            _runs[id] = cancel;
        }
    }

    public void Forget(Guid id)
    {
        lock (_gate)
        {
            _runs.Remove(id);
        }
    }

    public void Signal(Guid id)
    {
        CancellationTokenSource? cancel;
        lock (_gate)
        {
            _runs.TryGetValue(id, out cancel);
        }

        if (cancel is null)
        {
            return;
        }

        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
