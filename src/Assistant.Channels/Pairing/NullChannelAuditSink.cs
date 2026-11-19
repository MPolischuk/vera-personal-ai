using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Pairing;

internal sealed class NullChannelAuditSink : IChannelAuditSink
{
    public ValueTask RecordAsync(ChannelAuditEntry entry, CancellationToken ct) => ValueTask.CompletedTask;
}
