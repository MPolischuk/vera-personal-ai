using System.Security.Cryptography;
using System.Text;

namespace Assistant.Abstractions.Channels;

/// <summary>
/// Deduplicated channel inbox. The unique key is <c>(channel, external_id)</c>.
/// </summary>
public interface IChannelInboxStore
{
    /// <summary>
    /// Inserts the envelope. Returns null when the same channel and external id already exist.
    /// </summary>
    public ValueTask<long?> TryInsertAsync(InboundEnvelope envelope, CancellationToken ct);

    /// <summary>Loads one row, including a terminal row whose payload was cleared.</summary>
    public ValueTask<InboundItem?> FindAsync(long inboxId, CancellationToken ct);

    /// <summary>
    /// Rows still in <c>received</c>, plus <c>processing</c> older than five minutes, after <paramref name="afterId"/>.
    /// </summary>
    public ValueTask<IReadOnlyList<InboundItem>> ListRecoverableAsync(long afterId, int limit, CancellationToken ct);

    /// <summary>
    /// Sets the status. <paramref name="errorCode"/> is a short code, never message text.
    /// Terminal statuses clear the payload.
    /// </summary>
    public ValueTask MarkAsync(long inboxId, string status, string? errorCode, CancellationToken ct);

    /// <summary>Deletes rows for <paramref name="channel"/> older than <paramref name="retention"/>.</summary>
    public ValueTask<int> PurgeAsync(ChannelId channel, TimeSpan retention, CancellationToken ct);
}

/// <summary>
/// Closed kind of an inbound update. A new kind is a compile-time change at
/// <see cref="IInboxAcceptor"/>, which is the only place that chooses the queue.
/// </summary>
public abstract record InboundUpdateKind
{
    private InboundUpdateKind()
    {
    }

    /// <summary>A <c>message</c> update.</summary>
    public sealed record Message : InboundUpdateKind;

    /// <summary>A <c>callback_query</c> update.</summary>
    public sealed record CallbackQuery : InboundUpdateKind;

    /// <summary>A <c>my_chat_member</c> update.</summary>
    public sealed record MyChatMember : InboundUpdateKind;

    /// <summary>
    /// A <c>stopped_message_generation</c> update. It must not wait on the peer queue:
    /// the turn it cancels is the item already running there.
    /// </summary>
    public sealed record StoppedMessageGeneration : InboundUpdateKind;

    /// <summary>An update id and a peer were present, but the type is not one of the known ones.</summary>
    public sealed record Unknown : InboundUpdateKind;
}

/// <summary>Result of accepting one inbound envelope into the inbox.</summary>
/// <param name="Inserted">False when the external id was already stored.</param>
/// <param name="InboxId">New row id when <see cref="Inserted"/> is true.</param>
public readonly record struct InboxAcceptance(bool Inserted, long? InboxId);

/// <summary>
/// Inserts an envelope and then enqueues it. The queue decision for
/// <see cref="InboundUpdateKind"/> lives here so a future kind can skip the peer queue
/// without changing the endpoint.
/// </summary>
public interface IInboxAcceptor
{
    /// <summary>Persists the envelope and enqueues it when it is new.</summary>
    public ValueTask<InboxAcceptance> AcceptAsync(
        InboundEnvelope envelope,
        InboundUpdateKind kind,
        CancellationToken ct);
}

/// <summary>
/// Processes one inbox row. Pairing and the Telegram turn adapter replace the default later.
/// </summary>
public interface IInboxItemHandler
{
    /// <summary>Handles one deduplicated item.</summary>
    public ValueTask<InboundOutcome> HandleAsync(InboundItem item, CancellationToken ct);
}

/// <summary>Counters <c>assistant.channel.inbound</c> and <c>assistant.channel.webhook.rejected</c>.</summary>
public interface IChannelIngressTelemetry
{
    /// <summary>Counts one accepted or processed inbound update.</summary>
    public void Inbound(ChannelId channel, string outcome);

    /// <summary>Counts one rejected webhook request. <paramref name="reason"/> is <c>secret</c>, <c>size</c>, or <c>rate</c>.</summary>
    public void WebhookRejected(ChannelId channel, string reason);
}

/// <summary>Short hash of an external peer id for logs. The raw id is never written.</summary>
public static class ChannelPeerLogRef
{
    /// <summary>
    /// <c>tg:</c> plus eight hex characters for Telegram; <c>{channel}:</c> plus the same hash otherwise.
    /// </summary>
    public static string Format(ChannelId channel, string externalPeerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(externalPeerId ?? ""));
        var hex = Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
        return channel == ChannelId.Telegram ? "tg:" + hex : channel.Value + ":" + hex;
    }
}
