namespace Assistant.Abstractions.Channels;

/// <summary>
/// Audit row for a channel. No user text, pairing code, deep-link token, or raw peer id.
/// </summary>
/// <param name="Action">Event type, for example <c>channel.peer.approved</c>.</param>
/// <param name="Channel">Channel the event belongs to.</param>
/// <param name="PeerId">Peer id, when there is one.</param>
/// <param name="ConversationId">Conversation id, when there is one.</param>
/// <param name="ExternalMessageId">External message id, when there is one.</param>
/// <param name="Outcome">Short outcome label.</param>
/// <param name="DeviceId">Device that approved the peer. Only set for <c>channel.peer.approved</c> from the web.</param>
public sealed record ChannelAuditEntry(
    string Action,
    ChannelId Channel,
    Guid? PeerId,
    Guid? ConversationId,
    string? ExternalMessageId,
    string? Outcome,
    Guid? DeviceId = null);

/// <summary>
/// Writes channel audit entries. The API implementation stores them in the existing audit log.
/// </summary>
public interface IChannelAuditSink
{
    /// <summary>Records one entry.</summary>
    public ValueTask RecordAsync(ChannelAuditEntry entry, CancellationToken ct);
}
