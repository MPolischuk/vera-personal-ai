using System.Globalization;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Reminders;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Assistant.Channels.Telegram.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>
/// Turns an approved private update into a turn, a channel command, or a fixed reply.
/// Audio bytes are downloaded here. Transcription stays in the API.
/// </summary>
internal sealed class TelegramInboundChannel(
    ITelegramBotClient client,
    IOptions<TelegramOptions> options,
    ITurnIngress ingress,
    IChannelPeerDirectory peers,
    IChannelAuditSink audit,
    TelegramTurnSinkFactory sinks,
    TelegramSpeechAvailability speech,
    IEnumerable<IReminderQueryService> reminders,
    TelegramCallbackHandler callbacks,
    TelegramConfirmationMessages confirmations,
    ILogger<TelegramInboundChannel> logger) : IInboundChannel
{
    public const int MaxVoiceBytes = 20 * 1024 * 1024;

    public ChannelId Id => ChannelId.Telegram;

    public ChannelCapabilities Capabilities => TelegramCapabilities.For(options.Value);

    public InboundTrustPolicy TrustPolicy => InboundTrustPolicy.OwnerAuthoredTrusted;

    public async ValueTask<InboundOutcome> HandleAsync(InboundItem item, ChannelPeer peer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(peer);

        if (TelegramCallbackHandler.IsCallback(item.Envelope.RawPayloadJson))
        {
            return await callbacks.HandleAsync(item, peer, ct).ConfigureAwait(false);
        }

        switch (TelegramUpdateParser.Parse(item.Envelope.RawPayloadJson))
        {
            case TelegramParsedUpdate.Ignored:
                return InboundOutcome.Ignored;

            case TelegramParsedUpdate.Membership membership:
                return await ApplyMembershipAsync(item, peer, membership, ct).ConfigureAwait(false);

            case TelegramParsedUpdate.Command command:
                return await ExecuteCommandAsync(peer, command.Name, ct).ConfigureAwait(false);

            case TelegramParsedUpdate.Content content:
                return await SubmitContentAsync(item, peer, content, ct).ConfigureAwait(false);

            default:
                return InboundOutcome.Ignored;
        }
    }

    private async ValueTask<InboundOutcome> ApplyMembershipAsync(
        InboundItem item,
        ChannelPeer peer,
        TelegramParsedUpdate.Membership membership,
        CancellationToken ct)
    {
        await peers.SetDeliveryBlockedAsync(peer.Id, membership.DeliveryBlocked, ct).ConfigureAwait(false);
        if (membership.DeliveryBlocked)
        {
            await audit.RecordAsync(
                new ChannelAuditEntry(
                    "channel.peer.blocked_bot",
                    ChannelId.Telegram,
                    peer.Id,
                    peer.ConversationId,
                    item.Envelope.ExternalId,
                    "blocked"),
                ct).ConfigureAwait(false);
        }

        return InboundOutcome.CommandHandled;
    }

    private async ValueTask<InboundOutcome> ExecuteCommandAsync(ChannelPeer peer, string name, CancellationToken ct)
    {
        switch (name)
        {
            case TelegramCommands.Nueva:
                if (peer.UserId is not Guid userId)
                {
                    return InboundOutcome.Ignored;
                }

                await ingress.StartNewConversationAsync(ChannelId.Telegram, peer.Id, userId, ct).ConfigureAwait(false);
                await ReplyAsync(peer, TelegramReplies.NewConversation, ct).ConfigureAwait(false);
                return InboundOutcome.CommandHandled;

            case TelegramCommands.Recordatorios:
                await ReplyAsync(peer, await FormatRemindersAsync(peer, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                return InboundOutcome.CommandHandled;

            case TelegramCommands.Ayuda:
                await ReplyAsync(peer, TelegramReplies.Help, ct).ConfigureAwait(false);
                return InboundOutcome.CommandHandled;

            case TelegramCommands.Start:
                await ReplyAsync(peer, TelegramReplies.Start, ct).ConfigureAwait(false);
                return InboundOutcome.CommandHandled;

            default:
                return InboundOutcome.Ignored;
        }
    }

    private async ValueTask<string> FormatRemindersAsync(ChannelPeer peer, CancellationToken ct)
    {
        var query = reminders.FirstOrDefault();
        if (query is null || peer.UserId is not Guid userId)
        {
            return TelegramReplies.NoReminders;
        }

        var active = await query.ListActiveAsync(userId, ct).ConfigureAwait(false);
        if (active.Count == 0)
        {
            return TelegramReplies.NoReminders;
        }

        var lines = active.Select(item =>
            "- [ ] "
            + item.LocalDueLabel
            + " · "
            + item.Text
            + " ("
            + item.ShortRef.ToString(CultureInfo.InvariantCulture)
            + ")");
        return TelegramRichMarkdownSanitizer.Sanitize(string.Join('\n', lines));
    }

    private async ValueTask<InboundOutcome> SubmitContentAsync(
        InboundItem item,
        ChannelPeer peer,
        TelegramParsedUpdate.Content content,
        CancellationToken ct)
    {
        var parts = new List<InboundPart>(content.Parts);
        if (content.Voice is { } voice)
        {
            var attached = await TryAttachVoiceAsync(peer, voice, parts, ct).ConfigureAwait(false);
            if (!attached && !HasTurnContent(parts))
            {
                return InboundOutcome.CommandHandled;
            }
        }

        if (!HasTurnContent(parts))
        {
            if (parts.Count == 0)
            {
                return InboundOutcome.Ignored;
            }

            await ReplyAsync(peer, UnsupportedReply(parts), ct).ConfigureAwait(false);
            return InboundOutcome.CommandHandled;
        }

        if (peer.UserId is not Guid userId)
        {
            return InboundOutcome.Ignored;
        }

        var ack = await ingress.SubmitAsync(
            new ChannelTurnRequest(
                ChannelId.Telegram,
                peer.Id,
                userId,
                item.Envelope.ExternalId,
                parts,
                item.Envelope.ReceivedAt),
            sinks.Create(peer),
            ct).ConfigureAwait(false);

        if (ack.Accepted)
        {
            return InboundOutcome.TurnStarted;
        }

        if (ack.RejectionCode == "confirmation_pending")
        {
            await ResendConfirmationAsync(peer, ct).ConfigureAwait(false);
            return InboundOutcome.RejectedPending;
        }

        var message = TelegramReplies.ForTurnError(ack.RejectionCode);
        if (ack.RejectionCode == "unsupported_content")
        {
            message = UnsupportedReply(parts);
        }

        if (message is not null)
        {
            await ReplyAsync(peer, message, ct).ConfigureAwait(false);
        }

        return InboundOutcome.Failed;
    }

    private async ValueTask<bool> TryAttachVoiceAsync(
        ChannelPeer peer,
        TelegramVoiceNote voice,
        List<InboundPart> parts,
        CancellationToken ct)
    {
        var limit = TimeSpan.FromSeconds(Math.Max(1, options.Value.MaxVoiceSeconds));
        if (voice.Duration > limit)
        {
            await ReplyAsync(peer, TelegramReplies.VoiceTooLong, ct).ConfigureAwait(false);
            return false;
        }

        if (!speech.IsConfigured)
        {
            await ReplyAsync(peer, TelegramReplies.TextOnly, ct).ConfigureAwait(false);
            return false;
        }

        if (voice.FileSize is > MaxVoiceBytes)
        {
            await ReplyAsync(peer, TelegramReplies.FileTooLarge, ct).ConfigureAwait(false);
            return false;
        }

        try
        {
            var file = await client.GetFileAsync(voice.FileId, ct).ConfigureAwait(false);
            if (file.FileSize is > MaxVoiceBytes || string.IsNullOrWhiteSpace(file.FilePath))
            {
                await ReplyAsync(peer, TelegramReplies.FileTooLarge, ct).ConfigureAwait(false);
                return false;
            }

            await using var stream = await client.DownloadFileAsync(file.FilePath, ct).ConfigureAwait(false);
            var bytes = await ReadLimitedAsync(stream, ct).ConfigureAwait(false);
            if (bytes is null)
            {
                await ReplyAsync(peer, TelegramReplies.FileTooLarge, ct).ConfigureAwait(false);
                return false;
            }

            parts.Add(new InboundPart.Audio(bytes, "audio/ogg", voice.Duration, voice.Trust, voice.OriginLabel));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramInboundLog.VoiceFailed(logger, exception.GetType().Name, PeerRef(peer));
            await ReplyAsync(peer, TelegramReplies.SttFailed, ct).ConfigureAwait(false);
            return false;
        }
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var rented = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(rented, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxVoiceBytes)
            {
                return null;
            }

            buffer.Write(rented, 0, read);
        }
    }

    private async ValueTask ResendConfirmationAsync(ChannelPeer peer, CancellationToken ct)
    {
        if (!long.TryParse(peer.ExternalPeerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId)
            || confirmations.Find(chatId) is not { } card)
        {
            await ReplyAsync(peer, TelegramReplies.ConfirmFirst, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await TelegramHtmlMessage.SendAsync(
                client,
                chatId,
                card.Prompt + "\n\n" + TelegramReplies.ConfirmFirst,
                TelegramConfirmationRenderer.ConfirmationButtons(card.ConfirmationId),
                silent: false,
                retryOnRateLimit: true,
                TelegramTextSplitter.Gap,
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

    private async Task ReplyAsync(ChannelPeer peer, string text, CancellationToken ct)
    {
        if (!long.TryParse(peer.ExternalPeerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
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

    private static bool HasTurnContent(IReadOnlyList<InboundPart> parts)
        => parts.Any(part => part is InboundPart.Text { Value.Length: > 0 } or InboundPart.Audio);

    private static string UnsupportedReply(IReadOnlyList<InboundPart> parts)
    {
        foreach (var part in parts)
        {
            if (part is InboundPart.Unsupported { Kind: "audio" or "video_note" })
            {
                return TelegramReplies.VoiceOrText;
            }
        }

        return TelegramReplies.NoPhotos;
    }

    private static string PeerRef(ChannelPeer peer)
        => ChannelPeerLogRef.Format(ChannelId.Telegram, peer.ExternalPeerId);
}
