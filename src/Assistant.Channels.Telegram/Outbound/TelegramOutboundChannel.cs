using System.Globalization;
using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Assistant.Channels.Telegram.Webhook;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Delivers notifications to an approved Telegram peer. Provider failures become
/// <see cref="OutboundResult"/> values; this method does not throw for them.
/// </summary>
internal sealed class TelegramOutboundChannel(
    ITelegramBotClient client,
    IChannelPeerDirectory peers,
    IChannelPairingStore pairing,
    IChannelAuditSink audit,
    TelegramChannelGate gate,
    TimeProvider clock,
    ILogger<TelegramOutboundChannel> logger,
    IOptions<TelegramOptions>? options = null) : IOutboundChannel
{
    public ChannelId Id => ChannelId.Telegram;

    public ChannelCapabilities Capabilities => TelegramCapabilities.For(options?.Value ?? new TelegramOptions());

    public async ValueTask<ChannelReachability> GetReachabilityAsync(Guid userId, CancellationToken ct)
    {
        var peer = await peers.FindApprovedForUserAsync(Id, userId, ct).ConfigureAwait(false);
        if (peer is null || peer.Status != ChannelPeerStatus.Approved)
        {
            return new ChannelReachability(false, null, true, "no_peer");
        }

        if (peer.DeliveryBlocked)
        {
            return new ChannelReachability(false, null, true, "delivery_blocked");
        }

        return new ChannelReachability(true, peer.ExternalPeerId, true, null);
    }

    public async ValueTask<OutboundResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!long.TryParse(message.TargetRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
        {
            return new OutboundResult.PermanentFailure("target_invalid", DeactivateTarget: false);
        }

        if (TryBuildMarkup(message, out var markup) is { } invalid)
        {
            TelegramOutboundLog.ActionsInvalid(logger, PeerRef(message.TargetRef));
            return invalid;
        }

        var text = message.Kind == OutboundKind.Notification
            ? "⏰ " + message.Text
            : message.Text;

        try
        {
            var id = await TelegramHtmlMessage.SendAsync(
                client,
                chatId,
                text,
                markup,
                message.Silent,
                retryOnRateLimit: false,
                TelegramTextSplitter.Gap,
                ct).ConfigureAwait(false);
            return new OutboundResult.Delivered(id?.ToString(CultureInfo.InvariantCulture));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TelegramApiException exception)
        {
            return await MapApiFailureAsync(exception, message.UserId, message.TargetRef, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TelegramOutboundLog.SendFailed(logger, exception.GetType().Name, PeerRef(message.TargetRef));
            return new OutboundResult.TransientFailure(exception.GetType().Name, RetryAfter: null);
        }
    }

    private static OutboundResult.PermanentFailure? TryBuildMarkup(OutboundMessage message, out InlineKeyboardMarkup? markup)
    {
        markup = null;
        if (message.Kind == OutboundKind.ConfirmationRequest && message.ConfirmationId is Guid confirmationId)
        {
            markup = TelegramConfirmationRenderer.ConfirmationButtons(confirmationId);
            return null;
        }

        var actions = message.Actions;
        if (actions is not { Count: > 0 })
        {
            return null;
        }

        if (message.NotificationId is not Guid notificationId)
        {
            return new OutboundResult.PermanentFailure("actions_invalid", DeactivateTarget: false);
        }

        markup = TelegramConfirmationRenderer.NotificationButtons(notificationId, actions);
        return markup is null
            ? new OutboundResult.PermanentFailure("actions_invalid", DeactivateTarget: false)
            : null;
    }

    private async ValueTask<OutboundResult> MapApiFailureAsync(
        TelegramApiException exception, Guid userId, string targetRef, CancellationToken ct)
    {
        var result = TelegramErrorClassifier.Classify(exception);
        try
        {
            if (result is OutboundResult.PermanentFailure { Code: "403_blocked", DeactivateTarget: true })
            {
                var peer = await peers.FindApprovedForUserAsync(Id, userId, ct).ConfigureAwait(false);
                if (peer is not null)
                {
                    await peers.SetDeliveryBlockedAsync(peer.Id, blocked: true, ct).ConfigureAwait(false);
                }
            }
            else if (result is OutboundResult.PermanentFailure { Code: "chat_not_found" })
            {
                var peer = await peers.FindApprovedForUserAsync(Id, userId, ct).ConfigureAwait(false);
                if (peer is not null)
                {
                    await pairing.MarkRevokedAsync(peer.Id, clock.GetUtcNow(), ct).ConfigureAwait(false);
                    await audit.RecordAsync(
                        new ChannelAuditEntry(
                            "channel.peer.revoked",
                            Id,
                            peer.Id,
                            peer.ConversationId,
                            null,
                            "chat_not_found"),
                        ct).ConfigureAwait(false);
                }
            }
            else if (result is OutboundResult.PermanentFailure { Code: "unauthorized" })
            {
                TelegramOutboundLog.Unauthorized(logger);
                gate.Disable();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception sideEffect)
        {
            TelegramOutboundLog.SendFailed(logger, sideEffect.GetType().Name, PeerRef(targetRef));
        }

        return result;
    }

    private static string PeerRef(string targetRef)
        => ChannelPeerLogRef.Format(ChannelId.Telegram, targetRef);
}
