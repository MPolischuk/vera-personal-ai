using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Outbound;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>
/// Handles <c>stopped_message_generation</c> outside the peer queue.
/// A draft that is not the active one does nothing. A repeated update id is dropped by the inbox.
/// </summary>
internal sealed class TelegramStopHandler(ActiveDraftRegistry drafts, IChannelPeerDirectory peers, ITurnIngress ingress)
{
    public async Task AfterAcceptAsync(
        InboxAcceptance acceptance,
        InboundUpdateKind kind,
        string rawJson,
        IChannelInboxStore store,
        CancellationToken cancellationToken)
    {
        if (kind is not InboundUpdateKind.StoppedMessageGeneration
            || !acceptance.Inserted
            || acceptance.InboxId is not long inboxId)
        {
            return;
        }

        try
        {
            await HandleAsync(rawJson, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await store.MarkAsync(inboxId, "done", null, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleAsync(string rawJson, CancellationToken cancellationToken)
    {
        if (!TryRead(rawJson, out var chatId, out var draftId)
            || !drafts.TryGet(chatId, draftId, out var owner))
        {
            return;
        }

        var peer = await peers.FindAsync(
            ChannelId.Telegram,
            chatId.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);
        if (peer is null
            || peer.Status != ChannelPeerStatus.Approved
            || peer.Id != owner.PeerId
            || peer.UserId != owner.UserId)
        {
            return;
        }

        drafts.Remove(chatId, draftId);
        await ingress.InterruptAsync(ChannelId.Telegram, owner.PeerId, owner.UserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryRead(string rawJson, out long chatId, out int draftId)
    {
        chatId = 0;
        draftId = 0;
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("stopped_message_generation", out var stopped)
                || stopped.ValueKind != JsonValueKind.Object
                || !stopped.TryGetProperty("draft_id", out var draftElement)
                || !draftElement.TryGetInt32(out draftId)
                || draftId == 0
                || !stopped.TryGetProperty("chat", out var chat)
                || chat.ValueKind != JsonValueKind.Object
                || !chat.TryGetProperty("id", out var idElement)
                || !idElement.TryGetInt64(out chatId))
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
