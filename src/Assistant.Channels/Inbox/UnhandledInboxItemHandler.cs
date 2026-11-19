using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Inbox;

/// <summary>Stand-in until pairing (PT-81) and the Telegram adapter (PT-82) register a handler.</summary>
internal sealed class UnhandledInboxItemHandler : IInboxItemHandler
{
    public ValueTask<InboundOutcome> HandleAsync(InboundItem item, CancellationToken ct)
        => ValueTask.FromResult(InboundOutcome.Ignored);
}
