namespace Assistant.Abstractions.Proactive;

/// <summary>
/// The user's review list, one plain-text row.
/// Null means the row is missing. An empty string means the heartbeat must not call the model.
/// </summary>
public interface IHeartbeatChecklistStore
{
    public ValueTask<string?> GetAsync(Guid userId, CancellationToken ct);

    public ValueTask SetAsync(Guid userId, string text, CancellationToken ct);
}
