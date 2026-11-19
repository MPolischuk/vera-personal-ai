using Assistant.Abstractions.Channels;

namespace Assistant.Channels;

/// <summary>Channels registered in this process, keyed by <see cref="ChannelId"/>.</summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    private readonly Dictionary<ChannelId, IInboundChannel> _inbound;
    private readonly Dictionary<ChannelId, IOutboundChannel> _outbound;

    public ChannelRegistry(IEnumerable<IOutboundChannel> outbound, IEnumerable<IInboundChannel> inbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(inbound);

        var outboundList = new List<IOutboundChannel>();
        _outbound = [];
        foreach (var channel in outbound)
        {
            if (!_outbound.TryAdd(channel.Id, channel))
            {
                throw new InvalidOperationException($"Outbound channel '{channel.Id}' is registered more than once.");
            }

            outboundList.Add(channel);
        }

        Outbound = outboundList;
        _inbound = [];
        foreach (var channel in inbound)
        {
            if (!_inbound.TryAdd(channel.Id, channel))
            {
                throw new InvalidOperationException($"Inbound channel '{channel.Id}' is registered more than once.");
            }
        }
    }

    public IReadOnlyList<IOutboundChannel> Outbound { get; }

    public IInboundChannel? FindInbound(ChannelId id)
        => _inbound.TryGetValue(id, out var channel) ? channel : null;

    public IOutboundChannel? FindOutbound(ChannelId id)
        => _outbound.TryGetValue(id, out var channel) ? channel : null;
}
