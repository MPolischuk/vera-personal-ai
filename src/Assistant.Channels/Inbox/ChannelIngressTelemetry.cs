using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Inbox;

internal sealed class ChannelIngressTelemetry : IChannelIngressTelemetry
{
    public void Inbound(ChannelId channel, string outcome)
        => ChannelsTelemetry.Inbound.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel.Value),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void WebhookRejected(ChannelId channel, string reason)
        => ChannelsTelemetry.WebhookRejected.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel.Value),
            new KeyValuePair<string, object?>("reason", reason));
}
