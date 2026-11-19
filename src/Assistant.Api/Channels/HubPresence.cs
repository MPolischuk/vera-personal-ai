using System.Collections.Concurrent;

namespace Assistant.Api.Channels;

/// <summary>
/// Connections per user. Unknown visibility counts as reachable; a hidden tab does not.
/// Without <c>user.presence</c>, a connection stays unknown and the ack decides.
/// </summary>
internal sealed class HubPresence
{
    public const string Visible = "visible";

    public const string Hidden = "hidden";

    public const string Unknown = "unknown";

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, string>> _byUser = new();

    public void Connected(Guid userId, string connectionId)
    {
        var connections = _byUser.GetOrAdd(
            userId,
            _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
        connections[connectionId] = Unknown;
    }

    public void Disconnected(Guid userId, string connectionId)
    {
        if (!_byUser.TryGetValue(userId, out var connections))
        {
            return;
        }

        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty)
        {
            _byUser.TryRemove(userId, out _);
        }
    }

    public void SetVisibility(Guid userId, string connectionId, string visibility)
    {
        if (!_byUser.TryGetValue(userId, out var connections))
        {
            return;
        }

        if (connections.ContainsKey(connectionId))
        {
            connections[connectionId] = visibility;
        }
    }

    public bool IsReachable(Guid userId)
    {
        if (!_byUser.TryGetValue(userId, out var connections))
        {
            return false;
        }

        foreach (var visibility in connections.Values)
        {
            if (visibility is Visible or Unknown)
            {
                return true;
            }
        }

        return false;
    }
}
