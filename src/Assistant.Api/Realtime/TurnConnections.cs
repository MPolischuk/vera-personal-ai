using System.Collections.Concurrent;

namespace Assistant.Api.Realtime;

internal sealed record TurnOrigin(Guid TurnId, Guid ConversationId, Guid UserId, string ConnectionId);

internal sealed class TurnConnections
{
    // Broadcast entries outlive the stream briefly so the async bus can still
    // GroupExcept the originating connection for TurnCompleted (D4). Interrupt
    // lookups use the active map only, so a stale turnId cannot cancel a new turn.
    private static readonly TimeSpan BroadcastGrace = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, TurnOrigin> _active = new();
    private readonly ConcurrentDictionary<Guid, TurnOrigin> _broadcast = new();

    public IDisposable Register(TurnOrigin origin)
    {
        _active[origin.TurnId] = origin;
        _broadcast[origin.TurnId] = origin;
        return new Registration(this, origin.TurnId);
    }

    /// <summary>Active turns only — used by <c>user.interrupt</c>.</summary>
    public bool TryGetActive(Guid turnId, out TurnOrigin origin)
        => _active.TryGetValue(turnId, out origin!);

    /// <summary>Includes the short post-stream grace window — used by broadcast.</summary>
    public bool TryGet(Guid turnId, out TurnOrigin origin)
        => _broadcast.TryGetValue(turnId, out origin!);

    private sealed class Registration(TurnConnections owner, Guid turnId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            owner._active.TryRemove(turnId, out _);
            _ = ReleaseBroadcastLaterAsync(owner, turnId);
        }

        private static async Task ReleaseBroadcastLaterAsync(TurnConnections owner, Guid turnId)
        {
            try
            {
                await Task.Delay(BroadcastGrace).ConfigureAwait(false);
            }
            finally
            {
                owner._broadcast.TryRemove(turnId, out _);
            }
        }
    }
}
