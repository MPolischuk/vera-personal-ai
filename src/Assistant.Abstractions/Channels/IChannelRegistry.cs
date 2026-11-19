namespace Assistant.Abstractions.Channels;

/// <summary>
/// Registry of channels available in the system.
/// </summary>
public interface IChannelRegistry
{
    /// <summary>Registered outbound channels.</summary>
    public IReadOnlyList<IOutboundChannel> Outbound { get; }

    /// <summary>Finds an inbound channel by id.</summary>
    public IInboundChannel? FindInbound(ChannelId id);

    /// <summary>Finds an outbound channel by id.</summary>
    public IOutboundChannel? FindOutbound(ChannelId id);
}
