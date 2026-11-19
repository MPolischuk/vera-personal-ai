using System.Collections.Concurrent;

namespace Assistant.Channels.Pairing;

/// <summary>
/// Process-local copy of the current pairing code so a reminder can repeat it.
/// The database stores only the hash. A restart forgets the copy and the next reminder rotates the code.
/// </summary>
internal sealed class PairingCodeHold
{
    private readonly ConcurrentDictionary<Guid, Entry> _codes = new();

    public void Remember(Guid peerId, string displayCode, DateTimeOffset expiresAt)
        => _codes[peerId] = new Entry(displayCode, expiresAt);

    public string? Recall(Guid peerId, DateTimeOffset now)
    {
        if (!_codes.TryGetValue(peerId, out var entry) || entry.ExpiresAt <= now)
        {
            _codes.TryRemove(peerId, out _);
            return null;
        }

        return entry.Code;
    }

    public void Forget(Guid peerId) => _codes.TryRemove(peerId, out _);

    private sealed record Entry(string Code, DateTimeOffset ExpiresAt);
}
