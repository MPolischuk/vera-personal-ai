using System.Collections.Concurrent;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>In-memory map from the draft Telegram is showing to the peer that owns the turn.</summary>
internal sealed class ActiveDraftRegistry
{
    private readonly ConcurrentDictionary<DraftKey, DraftOwner> _entries = new();
    private int _nextId;

    public int NextDraftId()
    {
        while (true)
        {
            var id = Interlocked.Increment(ref _nextId);
            if (id != 0)
            {
                return id;
            }
        }
    }

    public void Add(long chatId, int draftId, Guid peerId, Guid userId)
        => _entries[new DraftKey(chatId, draftId)] = new DraftOwner(peerId, userId);

    public void Remove(long chatId, int draftId)
        => _entries.TryRemove(new DraftKey(chatId, draftId), out _);

    public bool TryGet(long chatId, int draftId, out DraftOwner owner)
        => _entries.TryGetValue(new DraftKey(chatId, draftId), out owner!);
}

internal readonly record struct DraftKey(long ChatId, int DraftId);

internal readonly record struct DraftOwner(Guid PeerId, Guid UserId);
