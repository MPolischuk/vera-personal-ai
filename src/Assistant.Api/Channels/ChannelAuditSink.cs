using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Core;
using Assistant.Core.Audit;

namespace Assistant.Api.Channels;

/// <summary>Stores channel audit entries on the existing audit log. The payload has no user text or secrets.</summary>
internal sealed class ChannelAuditSink(IAuditLogStore store) : IChannelAuditSink
{
    public ValueTask RecordAsync(ChannelAuditEntry entry, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new AuditPayload(
            entry.Channel.Value,
            entry.Outcome,
            entry.ExternalMessageId,
            entry.DeviceId));
        return store.AppendAsync(
            new AuditEntry(
                WellKnownIds.DefaultUser,
                entry.Action,
                "channel_peer",
                entry.PeerId ?? entry.ConversationId,
                payload),
            ct);
    }

    private sealed record AuditPayload(
        string Channel,
        string? Outcome,
        string? ExternalMessageId,
        Guid? DeviceId);
}
