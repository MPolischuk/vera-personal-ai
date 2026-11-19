using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>Builds a sink for confirmation expiry and for the inbound adapter.</summary>
internal sealed class TelegramTurnSinkFactory(
    ITelegramBotClient client,
    ILoggerFactory loggers,
    TelegramConfirmationMessages? confirmations = null,
    IOptions<TelegramOptions>? options = null,
    ActiveDraftRegistry? drafts = null,
    TimeProvider? clock = null) : IChannelTurnSinkFactory
{
    private readonly TelegramConfirmationMessages _confirmations = confirmations ?? new();

    public ChannelId Channel => ChannelId.Telegram;

    public ITurnSink Create(ChannelPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        DraftStreaming? streaming = null;
        var settings = options?.Value;
        if (drafts is not null
            && (settings?.UseDraftStreaming ?? true)
            && peer.UserId is Guid userId)
        {
            streaming = new DraftStreaming
            {
                Enabled = true,
                Registry = drafts,
                PeerId = peer.Id,
                UserId = userId,
                Clock = clock ?? TimeProvider.System,
                UpdateInterval = settings?.DraftUpdateInterval ?? TimeSpan.FromSeconds(1),
            };
        }

        return new TelegramTurnSink(
            client,
            peer.ExternalPeerId,
            loggers.CreateLogger<TelegramTurnSink>(),
            confirmations: _confirmations,
            useRichMessages: settings?.UseRichMessages ?? true,
            streaming: streaming);
    }
}

/// <summary>Draft streaming for one peer. Absent when the flag is off.</summary>
internal sealed class DraftStreaming
{
    public required bool Enabled { get; init; }

    public required ActiveDraftRegistry Registry { get; init; }

    public required Guid PeerId { get; init; }

    public required Guid UserId { get; init; }

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    public TimeSpan UpdateInterval { get; init; } = TimeSpan.FromSeconds(1);
}
