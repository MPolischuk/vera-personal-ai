namespace Assistant.Abstractions.Channels;

/// <summary>
/// Turn request from a channel.
/// </summary>
/// <param name="Channel">Origin channel.</param>
/// <param name="PeerId">Approved peer id.</param>
/// <param name="UserId">User of the peer.</param>
/// <param name="ExternalMessageId">External message id.</param>
/// <param name="Parts">Message parts (text, audio, and so on).</param>
/// <param name="SentAt">Time the message was sent.</param>
public sealed record ChannelTurnRequest(
    ChannelId Channel,
    Guid PeerId,
    Guid UserId,
    string ExternalMessageId,
    IReadOnlyList<InboundPart> Parts,
    DateTimeOffset SentAt);

/// <summary>
/// Confirmation response from a channel.
/// </summary>
/// <param name="Channel">Origin channel.</param>
/// <param name="PeerId">Peer id.</param>
/// <param name="UserId">User of the peer.</param>
/// <param name="ConfirmationId">Confirmation id.</param>
/// <param name="Approved">Whether the user approved or rejected.</param>
/// <param name="ExternalMessageId">External id of the reply message.</param>
public sealed record ChannelConfirmationResponse(
    ChannelId Channel,
    Guid PeerId,
    Guid UserId,
    Guid ConfirmationId,
    bool Approved,
    string ExternalMessageId);

/// <summary>
/// Turn outcome for the adapter.
/// </summary>
public enum ChannelTurnOutcome
{
    /// <summary>Completed successfully.</summary>
    Completed,

    /// <summary>Waiting for a confirmation.</summary>
    AwaitingConfirmation,

    /// <summary>Interrupted by the user.</summary>
    Interrupted,

    /// <summary>Failed.</summary>
    Failed,

    /// <summary>Rejected (validation, a turn already in progress, and so on).</summary>
    Rejected
}

/// <summary>
/// Full turn result.
/// </summary>
/// <param name="Outcome">Turn outcome.</param>
/// <param name="FullText">Accumulated full text (channels without streaming).</param>
/// <param name="ErrorCode">Error code when the turn failed.</param>
/// <param name="MessageId">Id of the generated message.</param>
public sealed record ChannelTurnResult(
    ChannelTurnOutcome Outcome,
    string? FullText,
    string? ErrorCode,
    Guid? MessageId);

/// <summary>
/// Confirmation prompt for the channel.
/// </summary>
/// <param name="ConfirmationId">Confirmation id.</param>
/// <param name="ToolName">Name of the tool that requires confirmation.</param>
/// <param name="Prompt">Prompt text.</param>
/// <param name="ExpiresAt">When the confirmation expires.</param>
public sealed record ChannelConfirmationPrompt(
    Guid ConfirmationId,
    string ToolName,
    string Prompt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Sink of turn updates for the adapter.
/// Receives neutral updates (no Core types).
/// </summary>
public interface ITurnSink
{
    /// <summary>The turn was accepted and started.</summary>
    public ValueTask OnTurnStartedAsync(Guid turnId, CancellationToken ct);

    /// <summary>Echo of the audio transcription.</summary>
    public ValueTask OnTranscriptionAsync(string text, CancellationToken ct);

    /// <summary>Text delta of the reply (already without <c>[[cue:…]]</c>).</summary>
    public ValueTask OnTextDeltaAsync(string text, CancellationToken ct);

    /// <summary>Activity of a tool.</summary>
    public ValueTask OnToolActivityAsync(string toolName, bool completed, CancellationToken ct);

    /// <summary>A confirmation is required.</summary>
    public ValueTask OnConfirmationRequestedAsync(ChannelConfirmationPrompt prompt, CancellationToken ct);

    /// <summary>The turn finished (exactly one call).</summary>
    public ValueTask OnTurnFinishedAsync(ChannelTurnResult result, CancellationToken ct);
}

/// <summary>
/// Acknowledgement from ingress back to the adapter.
/// </summary>
/// <param name="Accepted">Whether the request was accepted.</param>
/// <param name="RejectionCode">Rejection code when it was not accepted.</param>
public sealed record TurnIngressAck(bool Accepted, string? RejectionCode);

/// <summary>
/// Shared turn entry point for channels.
/// </summary>
public interface ITurnIngress
{
    /// <summary>
    /// Submits a turn from a channel.
    /// </summary>
    /// <param name="request">Turn request.</param>
    /// <param name="sink">Update sink for the adapter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ingress acknowledgement.</returns>
    public ValueTask<TurnIngressAck> SubmitAsync(
        ChannelTurnRequest request,
        ITurnSink sink,
        CancellationToken ct);

    /// <summary>
    /// Resolves a confirmation from a channel.
    /// </summary>
    /// <param name="response">Confirmation response.</param>
    /// <param name="sink">Update sink for the adapter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ingress acknowledgement.</returns>
    public ValueTask<TurnIngressAck> ResolveConfirmationAsync(
        ChannelConfirmationResponse response,
        ITurnSink sink,
        CancellationToken ct);

    /// <summary>
    /// Starts a new conversation for the peer (<c>/nueva</c> on Telegram).
    /// </summary>
    /// <param name="channel">Origin channel.</param>
    /// <param name="peerId">Peer id.</param>
    /// <param name="userId">User of the peer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Id of the created conversation.</returns>
    public ValueTask<Guid> StartNewConversationAsync(
        ChannelId channel,
        Guid peerId,
        Guid userId,
        CancellationToken ct);

    /// <summary>
    /// Interrupts the in-progress turn of the peer's conversation.
    /// </summary>
    /// <param name="channel">Origin channel.</param>
    /// <param name="peerId">Peer id.</param>
    /// <param name="userId">User of the peer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ingress acknowledgement.</returns>
    /// <remarks>
    /// Cuts the in-progress turn of the peer's conversation. Does not throw.
    /// </remarks>
    public ValueTask<TurnIngressAck> InterruptAsync(
        ChannelId channel,
        Guid peerId,
        Guid userId,
        CancellationToken ct);
}

/// <summary>
/// Factory of turn sinks for a channel, so ConfirmationExpiryService can deliver
/// the close of an expired confirmation on the correct channel.
/// </summary>
public interface IChannelTurnSinkFactory
{
    /// <summary>Channel the factory belongs to.</summary>
    public ChannelId Channel { get; }

    /// <summary>
    /// Creates a sink for a specific peer.
    /// </summary>
    public ITurnSink Create(ChannelPeer peer);
}
