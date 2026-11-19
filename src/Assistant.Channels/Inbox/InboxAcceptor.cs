using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Inbox;

/// <summary>
/// Inserts, then enqueues on the peer queue. <see cref="InboundUpdateKind.StoppedMessageGeneration"/>
/// is stored for deduplication and left for the caller to dispatch immediately.
/// </summary>
internal sealed class InboxAcceptor(
    IChannelInboxStore store,
    InboxProcessor processor,
    IChannelIngressTelemetry telemetry) : IInboxAcceptor
{
    public async ValueTask<InboxAcceptance> AcceptAsync(
        InboundEnvelope envelope,
        InboundUpdateKind kind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(kind);

        var id = await store.TryInsertAsync(envelope, ct);
        if (id is null)
        {
            telemetry.Inbound(envelope.Channel, "duplicate");
            return new InboxAcceptance(Inserted: false, InboxId: null);
        }

        switch (Route(kind))
        {
            case InboxRoute.PeerQueue:
                processor.Enqueue(id.Value, InboxPeerKey.Format(envelope));
                break;
            case InboxRoute.Immediate:
                break;
        }

        return new InboxAcceptance(Inserted: true, id);
    }

    /// <summary>
    /// Single routing point. Adding a kind fails the build until this switch names its queue.
    /// </summary>
    private static InboxRoute Route(InboundUpdateKind kind) => kind switch
    {
        InboundUpdateKind.Message => InboxRoute.PeerQueue,
        InboundUpdateKind.CallbackQuery => InboxRoute.PeerQueue,
        InboundUpdateKind.MyChatMember => InboxRoute.PeerQueue,
        InboundUpdateKind.StoppedMessageGeneration => InboxRoute.Immediate,
        InboundUpdateKind.Unknown => InboxRoute.PeerQueue,
        _ => throw new InvalidOperationException($"No inbox route for {kind.GetType().Name}."),
    };

    private enum InboxRoute
    {
        PeerQueue,

        /// <summary>Stored, not enqueued. The channel dispatches it before the HTTP call returns.</summary>
        Immediate,
    }
}
