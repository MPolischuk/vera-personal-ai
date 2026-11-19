using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Inbox;

internal static class InboxPeerKey
{
    public static string Format(InboundEnvelope envelope)
        => envelope.Channel.Value + "\n" + envelope.ExternalPeerId;
}
