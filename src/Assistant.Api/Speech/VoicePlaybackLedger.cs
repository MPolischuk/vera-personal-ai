using System.Collections.Concurrent;

namespace Assistant.Api.Speech;

/// <summary>
/// Per-turn map of spoken sentence ranges for E6 trim. Survives <c>ActiveTurns</c>.
/// </summary>
internal sealed class VoicePlaybackLedger
{
    private readonly ConcurrentDictionary<Guid, TurnLedger> _byTurn = new();

    public TurnLedger GetOrCreate(Guid turnId, Guid conversationId, Guid userId)
        => _byTurn.GetOrAdd(turnId, _ => new TurnLedger(turnId, conversationId, userId));

    public bool TryGet(Guid turnId, out TurnLedger ledger)
        => _byTurn.TryGetValue(turnId, out ledger!);

    public void Drop(Guid turnId) => _byTurn.TryRemove(turnId, out _);

    public void DropConversationExcept(Guid conversationId, Guid keepTurnId)
    {
        foreach (var pair in _byTurn)
        {
            if (pair.Value.ConversationId == conversationId && pair.Key != keepTurnId)
            {
                _byTurn.TryRemove(pair.Key, out _);
            }
        }
    }
}

internal sealed class TurnLedger(Guid turnId, Guid conversationId, Guid userId)
{
    private readonly Lock _gate = new();
    private readonly List<SpokenSentence> _sentences = [];

    public Guid TurnId { get; } = turnId;

    public Guid ConversationId { get; } = conversationId;

    public Guid UserId { get; } = userId;

    public Guid? MessageId { get; set; }

    public int? PendingPlayedChunks { get; set; }

    public void AddSentence(string text, int seqStart, int seqEnd)
    {
        lock (_gate)
        {
            _sentences.Add(new SpokenSentence(text, seqStart, seqEnd));
        }
    }

    public bool TryBuildSpokenText(int playedChunks, out string spoken)
    {
        lock (_gate)
        {
            var parts = _sentences
                .Where(s => s.SeqStart < playedChunks)
                .Select(s => s.Text)
                .ToList();

            spoken = parts.Count == 0 ? string.Empty : string.Join(' ', parts);
            return true;
        }
    }
}

internal sealed record SpokenSentence(string Text, int SeqStart, int SeqEnd);
