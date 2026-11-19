using System.Collections.Concurrent;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>How an open confirmation card is about to close. Expiry leaves it <see cref="Open"/>.</summary>
internal enum ConfirmationClose
{
    Open,
    Approved,
    Denied,
}

/// <summary>The Telegram message that carries a confirmation card, so a later sink can clear it.</summary>
internal sealed record TelegramConfirmationCard(
    Guid ConfirmationId,
    long ChatId,
    long MessageId,
    string Prompt,
    ConfirmationClose Close);

/// <summary>
/// Process-wide map from a chat to its open confirmation card.
/// The expiry sink is a new instance and would not otherwise know the message id.
/// </summary>
internal sealed class TelegramConfirmationMessages
{
    private readonly ConcurrentDictionary<long, TelegramConfirmationCard> _byChat = new();

    public void Remember(TelegramConfirmationCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _byChat[card.ChatId] = card;
    }

    public TelegramConfirmationCard? Find(long chatId)
        => _byChat.TryGetValue(chatId, out var card) ? card : null;

    public void Mark(long chatId, ConfirmationClose close)
    {
        if (_byChat.TryGetValue(chatId, out var card))
        {
            _byChat[chatId] = card with { Close = close };
        }
    }

    public bool TryTake(long chatId, out TelegramConfirmationCard card)
        => _byChat.TryRemove(chatId, out card!);

    public void Forget(long chatId) => _byChat.TryRemove(chatId, out _);
}
