namespace Assistant.Abstractions.Channels;

/// <summary>
/// A communication channel with the user.
/// </summary>
public interface IChannel
{
    /// <summary>Channel identifier.</summary>
    public ChannelId Id { get; }

    /// <summary>Channel capabilities.</summary>
    public ChannelCapabilities Capabilities { get; }
}

/// <summary>
/// A channel the user can write through. The adapter turns the native payload
/// into an <see cref="InboundEnvelope"/> and leaves it in the inbox; it never
/// calls the orchestrator directly.
/// </summary>
public interface IInboundChannel : IChannel
{
    /// <summary>
    /// How text written by the owner in their paired chat is treated.
    /// </summary>
    public InboundTrustPolicy TrustPolicy { get; }

    /// <summary>
    /// Processes an item that is already deduplicated and paired: builds parts,
    /// calls <see cref="ITurnIngress"/> with its own sink, and resolves channel
    /// commands (<c>/nueva</c>, <c>/ayuda</c>).
    /// </summary>
    /// <param name="item">Inbox item.</param>
    /// <param name="peer">Approved peer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Processing outcome.</returns>
    public ValueTask<InboundOutcome> HandleAsync(InboundItem item, ChannelPeer peer, CancellationToken ct);
}

/// <summary>
/// A channel through which Vera can reach the user without the user having written first.
/// </summary>
/// <remarks>
/// Invariants every adapter guarantees (and that the conformance kit tests):
/// <list type="number">
/// <item><see cref="SendAsync"/> <b>does not throw</b> on provider failures: it returns
/// <see cref="OutboundResult.TransientFailure"/> or <see cref="OutboundResult.PermanentFailure"/>.
/// It throws only for a bug (invalid argument) or cancellation.</item>
/// <item>If <c>Text.Length</c> exceeds <see cref="ChannelCapabilities.MaxTextLength"/>, the adapter
/// splits the message (Telegram) or truncates it with an ellipsis (push).
/// It never fails because of length.</item>
/// <item>If <c>Actions.Count</c> exceeds <see cref="ChannelCapabilities.MaxButtons"/>, or an encoded
/// <c>ActionId</c> exceeds <see cref="ChannelCapabilities.MaxActionPayloadBytes"/>, that is a caller bug:
/// return <c>PermanentFailure("actions_invalid")</c> and log an error.</item>
/// <item>The router does not retry a <see cref="OutboundResult.Delivered"/>; on push, the topic or tag
/// collapses an accidental resend.</item>
/// <item>Never log <c>Text</c>, a full <c>TargetRef</c>, tokens, or push endpoints.</item>
/// </list>
/// </remarks>
public interface IOutboundChannel : IChannel
{
    /// <summary>
    /// Checks whether the user can be reached right now.
    /// </summary>
    /// <param name="userId">Recipient user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Reachability of the user on this channel.</returns>
    /// <remarks>
    /// hub: client connected; webpush: active subscription;
    /// telegram: approved peer that is not blocked; whatsapp: peer plus window state.
    /// </remarks>
    public ValueTask<ChannelReachability> GetReachabilityAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Sends a message to the user.
    /// </summary>
    /// <param name="message">Message to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Send result.</returns>
    public ValueTask<OutboundResult> SendAsync(OutboundMessage message, CancellationToken ct);
}
