namespace Assistant.Abstractions.Channels;

/// <summary>
/// Trust level of inbound content.
/// </summary>
public enum ContentTrust
{
    /// <summary>Written by the paired owner in their chat.</summary>
    OwnerAuthored = 0,

    /// <summary>Forwarded, quoted, or from an external origin (untrusted).</summary>
    Untrusted = 1
}

/// <summary>
/// Channel trust policy for inbound content.
/// </summary>
public enum InboundTrustPolicy
{
    /// <summary>
    /// What the paired owner writes is user input; forwarded, quoted, or external content is untrusted.
    /// </summary>
    OwnerAuthoredTrusted = 0,

    /// <summary>
    /// Everything that arrives is untrusted (conservative).
    /// </summary>
    AllUntrusted = 1
}

/// <summary>
/// Raw wrapper of an inbound message, as it arrived from the webhook.
/// The native payload is kept raw so it can be reprocessed if the process dies.
/// </summary>
/// <param name="Channel">Origin channel.</param>
/// <param name="ExternalId">External message id (Telegram: update_id; WhatsApp: wamid).</param>
/// <param name="ExternalPeerId">External sender id (Telegram: chat.id; WhatsApp: wa_id).</param>
/// <param name="PeerDisplayName">Visible name of the sender.</param>
/// <param name="ReceivedAt">Time the message was received.</param>
/// <param name="RawPayloadJson">Raw webhook JSON payload.</param>
public sealed record InboundEnvelope(
    ChannelId Channel,
    string ExternalId,
    string ExternalPeerId,
    string? PeerDisplayName,
    DateTimeOffset ReceivedAt,
    string RawPayloadJson);

/// <summary>
/// Inbox item already deduplicated and ready to process.
/// </summary>
/// <param name="InboxId">Id in the channel_inbox table.</param>
/// <param name="Envelope">Message envelope.</param>
/// <param name="Attempt">Processing attempt number.</param>
public sealed record InboundItem(
    long InboxId,
    InboundEnvelope Envelope,
    int Attempt);

/// <summary>
/// Parsed part of an inbound message. A closed record hierarchy.
/// </summary>
public abstract record InboundPart
{
    /// <summary>Text.</summary>
    /// <param name="Value">Text content.</param>
    /// <param name="Trust">Trust level.</param>
    /// <param name="OriginLabel">Origin label (for forwarded content).</param>
    public sealed record Text(
        string Value,
        ContentTrust Trust,
        string? OriginLabel = null) : InboundPart;

    /// <summary>Audio (voice note).</summary>
    /// <param name="Data">Audio bytes.</param>
    /// <param name="MimeType">MIME type (for example, "audio/ogg").</param>
    /// <param name="Duration">Audio duration.</param>
    /// <param name="Trust">Trust level.</param>
    /// <param name="OriginLabel">Origin label when the note was forwarded.</param>
    public sealed record Audio(
        ReadOnlyMemory<byte> Data,
        string MimeType,
        TimeSpan Duration,
        ContentTrust Trust,
        string? OriginLabel = null) : InboundPart;

    /// <summary>Unsupported content.</summary>
    /// <param name="Kind">Content kind (for example, "photo", "sticker", "document").</param>
    public sealed record Unsupported(string Kind) : InboundPart;
}

/// <summary>
/// Outcome of processing an inbound item.
/// </summary>
public enum InboundOutcome
{
    /// <summary>A turn started successfully.</summary>
    TurnStarted,

    /// <summary>A channel command was handled (no model).</summary>
    CommandHandled,

    /// <summary>A confirmation was resolved.</summary>
    ConfirmationResolved,

    /// <summary>Ignored (peer not approved, rate limit).</summary>
    Ignored,

    /// <summary>Rejected because a confirmation is pending.</summary>
    RejectedPending,

    /// <summary>Processing failed.</summary>
    Failed
}
