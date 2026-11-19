using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Assistant.Channels.Telegram.Outbound;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>
/// Answers a <c>callback_query</c> before any long work, then resumes a confirmation
/// or routes a notification button. A missing notification service does not fail the update.
/// </summary>
internal sealed class TelegramCallbackHandler(
    ITelegramBotClient client,
    ITurnIngress ingress,
    TelegramTurnSinkFactory sinks,
    TelegramConfirmationMessages confirmations,
    IEnumerable<INotificationActionService> notificationActions,
    ILogger<TelegramCallbackHandler> logger)
{
    public const string AlreadyResolvedCode = "confirmation_already_resolved";

    public static bool IsCallback(string? rawJson)
        => TryRead(rawJson, out _);

    public async ValueTask<InboundOutcome> HandleAsync(InboundItem item, ChannelPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(peer);
        if (!TryRead(item.Envelope.RawPayloadJson, out var query))
        {
            return InboundOutcome.Ignored;
        }

        try
        {
            return await HandleQueryAsync(query, peer, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramInboundLog.ReplyFailed(logger, exception.GetType().Name, PeerRef(peer));
            return InboundOutcome.Failed;
        }
    }

    private async ValueTask<InboundOutcome> HandleQueryAsync(
        TelegramCallback query, ChannelPeer peer, CancellationToken ct)
    {
        var fromPeer = query.FromId.ToString(CultureInfo.InvariantCulture) == peer.ExternalPeerId;
        var payload = fromPeer ? CallbackData.Decode(query.Data) : null;
        if (!fromPeer || payload is null)
        {
            await AnswerAsync(query.Id, TelegramReplies.NoLongerCurrent, ct).ConfigureAwait(false);
            if (fromPeer && query.ChatId is long chatId && query.MessageId is long messageId)
            {
                await ClearAsync(chatId, messageId, ct).ConfigureAwait(false);
            }

            return InboundOutcome.CommandHandled;
        }

        if (payload is CallbackPayload.Notification notification
            && (notificationActions.FirstOrDefault() is null || peer.UserId is null))
        {
            // No action service yet: the sentence is the whole reply, and there is no long work.
            await AnswerAsync(query.Id, TelegramReplies.NoLongerCurrent, ct).ConfigureAwait(false);
            return InboundOutcome.CommandHandled;
        }

        // Dismiss the spinner before the orchestrator or a notification action runs.
        await AnswerAsync(query.Id, text: null, ct).ConfigureAwait(false);
        return payload switch
        {
            CallbackPayload.Confirmation confirmation => await ResolveConfirmationAsync(query, peer, confirmation, ct)
                .ConfigureAwait(false),
            CallbackPayload.Notification notificationAction => await ResolveNotificationAsync(
                query, peer, notificationAction, ct).ConfigureAwait(false),
            _ => InboundOutcome.CommandHandled,
        };
    }

    private async ValueTask<InboundOutcome> ResolveConfirmationAsync(
        TelegramCallback query,
        ChannelPeer peer,
        CallbackPayload.Confirmation confirmation,
        CancellationToken ct)
    {
        if (peer.UserId is not Guid userId)
        {
            await SendPlainAsync(peer, TelegramReplies.NoLongerCurrent, ct).ConfigureAwait(false);
            return InboundOutcome.CommandHandled;
        }

        var chatId = query.ChatId ?? ParseChat(peer);
        var tracked = chatId is long known && confirmations.Find(known) is not null;
        if (chatId is long markChat)
        {
            confirmations.Mark(markChat, confirmation.Approved ? ConfirmationClose.Approved : ConfirmationClose.Denied);
        }

        var ack = await ingress.ResolveConfirmationAsync(
            new ChannelConfirmationResponse(
                ChannelId.Telegram,
                peer.Id,
                userId,
                confirmation.Id,
                confirmation.Approved,
                (query.MessageId?.ToString(CultureInfo.InvariantCulture)) ?? query.Id),
            sinks.Create(peer),
            ct).ConfigureAwait(false);

        if (!ack.Accepted)
        {
            if (chatId is long forgetChat)
            {
                confirmations.Forget(forgetChat);
            }

            await SendPlainAsync(
                peer,
                ack.RejectionCode == AlreadyResolvedCode
                    ? TelegramReplies.AlreadyResolved
                    : TelegramReplies.NoLongerCurrent,
                ct).ConfigureAwait(false);
            if (chatId is long clearChat && query.MessageId is long clearMessage)
            {
                await ClearAsync(clearChat, clearMessage, ct).ConfigureAwait(false);
            }

            return InboundOutcome.CommandHandled;
        }

        if (chatId is long okChat && query.MessageId is long okMessage)
        {
            await ClearAsync(okChat, okMessage, ct).ConfigureAwait(false);
        }

        if (!confirmation.Approved && !tracked)
        {
            await SendPlainAsync(peer, TelegramReplies.ConfirmationDenied, ct).ConfigureAwait(false);
        }

        return InboundOutcome.ConfirmationResolved;
    }

    private async ValueTask<InboundOutcome> ResolveNotificationAsync(
        TelegramCallback query,
        ChannelPeer peer,
        CallbackPayload.Notification notification,
        CancellationToken ct)
    {
        var service = notificationActions.FirstOrDefault();
        if (service is null || peer.UserId is not Guid userId)
        {
            await SendPlainAsync(peer, TelegramReplies.NoLongerCurrent, ct).ConfigureAwait(false);
            return InboundOutcome.CommandHandled;
        }

        var applied = await service.TryExecuteAsync(userId, notification.Id, notification.ActionId, ct)
            .ConfigureAwait(false);
        if (!applied)
        {
            await SendPlainAsync(peer, TelegramReplies.NoLongerCurrent, ct).ConfigureAwait(false);
        }
        else if (query.ChatId is long chatId && query.MessageId is long messageId)
        {
            await ClearAsync(chatId, messageId, ct).ConfigureAwait(false);
        }

        return InboundOutcome.CommandHandled;
    }

    private async Task AnswerAsync(string callbackId, string? text, CancellationToken ct)
    {
        try
        {
            await client.AnswerCallbackQueryAsync(
                new AnswerCallbackQueryRequest
                {
                    CallbackQueryId = callbackId,
                    Text = text,
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramInboundLog.ReplyFailed(logger, exception.GetType().Name, "tg:callback");
        }
    }

    private async Task SendPlainAsync(ChannelPeer peer, string text, CancellationToken ct)
    {
        if (ParseChat(peer) is not long chatId)
        {
            return;
        }

        try
        {
            await client.SendMessageAsync(
                new SendMessageRequest
                {
                    ChatId = chatId,
                    Text = text,
                    ParseMode = null,
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramInboundLog.ReplyFailed(logger, exception.GetType().Name, PeerRef(peer));
        }
    }

    private async Task ClearAsync(long chatId, long messageId, CancellationToken ct)
    {
        try
        {
            await TelegramMarkup.ClearAsync(client, chatId, messageId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramInboundLog.ReplyFailed(logger, exception.GetType().Name, "tg:callback");
        }
    }

    private static long? ParseChat(ChannelPeer peer)
        => long.TryParse(peer.ExternalPeerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId)
            ? chatId
            : null;

    private static string PeerRef(ChannelPeer peer)
        => ChannelPeerLogRef.Format(ChannelId.Telegram, peer.ExternalPeerId);

    internal static bool TryRead(string? rawJson, out TelegramCallback query)
    {
        query = null!;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("callback_query", out var callback)
                || callback.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var id = ReadString(callback, "id");
            if (id is null || !TryReadInt64(callback, "from", "id", out var fromId))
            {
                return false;
            }

            long? chatId = null;
            long? messageId = null;
            if (callback.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                if (TryReadInt64(message, "chat", "id", out var parsedChat))
                {
                    chatId = parsedChat;
                }

                if (message.TryGetProperty("message_id", out var messageIdElement)
                    && messageIdElement.TryGetInt64(out var parsedMessage))
                {
                    messageId = parsedMessage;
                }
            }

            query = new TelegramCallback(id, fromId, chatId, messageId, ReadString(callback, "data"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadInt64(JsonElement owner, string objectName, string property, out long value)
    {
        value = 0;
        return owner.TryGetProperty(objectName, out var child)
               && child.ValueKind == JsonValueKind.Object
               && child.TryGetProperty(property, out var id)
               && id.TryGetInt64(out value);
    }

    private static string? ReadString(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

/// <summary>Fields the callback handler needs. The raw <c>callback_data</c> is not logged.</summary>
internal sealed record TelegramCallback(
    string Id,
    long FromId,
    long? ChatId,
    long? MessageId,
    string? Data);
