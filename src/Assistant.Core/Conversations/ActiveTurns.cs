using System.Collections.Concurrent;

namespace Assistant.Core.Conversations;

internal sealed class ActiveTurns : IActiveTurns
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _inProgress = new();

    /// <summary>Null if a turn is already in progress for that conversation (D7).</summary>
    public IDisposable? TryRegister(Guid conversationId, CancellationToken outer, out CancellationToken turnToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        if (!_inProgress.TryAdd(conversationId, linked))
        {
            linked.Dispose();
            turnToken = default;
            return null;
        }

        turnToken = linked.Token;
        return new Registration(this, conversationId, linked);
    }

    public bool IsInProgress(Guid conversationId) => _inProgress.ContainsKey(conversationId);

    public bool TryInterrupt(Guid conversationId)
    {
        if (!_inProgress.TryGetValue(conversationId, out var source))
        {
            return false;
        }

        try
        {
            if (source.IsCancellationRequested)
            {
                return false;
            }

            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private sealed class Registration(
        ActiveTurns owner, Guid conversationId, CancellationTokenSource source) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            if (owner._inProgress.TryRemove(conversationId, out var current) && ReferenceEquals(current, source))
            {
                source.Dispose();
            }
            else
            {
                source.Dispose();
            }
        }
    }
}
