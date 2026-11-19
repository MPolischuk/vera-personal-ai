using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>Hands an approved Telegram inbox row to <see cref="TelegramInboundChannel"/>.</summary>
internal sealed class TelegramInboxItemHandler(IChannelPeerDirectory peers, TelegramInboundChannel channel) : IInboxItemHandler
{
    public async ValueTask<InboundOutcome> HandleAsync(InboundItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Envelope.Channel != ChannelId.Telegram)
        {
            return InboundOutcome.Ignored;
        }

        var peer = await peers.FindAsync(item.Envelope.Channel, item.Envelope.ExternalPeerId, ct).ConfigureAwait(false);
        if (peer is null || peer.Status != ChannelPeerStatus.Approved || peer.UserId is null)
        {
            return InboundOutcome.Ignored;
        }

        return await channel.HandleAsync(item, peer, ct).ConfigureAwait(false);
    }
}
