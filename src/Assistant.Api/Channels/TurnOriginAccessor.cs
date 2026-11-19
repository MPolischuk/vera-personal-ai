using Assistant.Abstractions.Channels;

namespace Assistant.Api.Channels;

internal sealed class TurnOriginAccessor : ITurnOriginAccessor
{
    public TurnOrigin Current { get; private set; } = new(ChannelId.Hub, null);

    public void Set(TurnOrigin origin) => Current = origin;
}
