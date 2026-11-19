using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Assistant.Abstractions.Channels;
using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;
using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// One sink per turn. Streams a rich draft while the model runs, or types when drafts are off.
/// The final reply is a rich message, with the HTML path as fallback.
/// A confirmation is an HTML message with Confirmar / Cancelar. A later sink clears those buttons.
/// </summary>
internal sealed partial class TelegramTurnSink : ITurnSink
{
    private readonly ITelegramBotClient _client;
    private readonly long? _chatId;
    private readonly string _peerRef;
    private readonly ILogger _logger;
    private readonly TimeSpan _typingInterval;
    private readonly TimeSpan _partGap;
    private readonly TelegramConfirmationMessages? _confirmations;
    private readonly bool _useRichMessages;
    private readonly DraftStreaming? _streaming;
    private readonly StringBuilder _accumulated = new();
    private CancellationTokenSource? _typing;
    private Task? _typingTask;
    private DraftStreamer? _draft;
    private bool _requestedConfirmation;

    public TelegramTurnSink(
        ITelegramBotClient client,
        string externalPeerId,
        ILogger<TelegramTurnSink> logger,
        TimeSpan? typingInterval = null,
        TimeSpan? partGap = null,
        TelegramConfirmationMessages? confirmations = null,
        bool useRichMessages = true,
        DraftStreaming? streaming = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = logger;
        _confirmations = confirmations;
        _useRichMessages = useRichMessages;
        _streaming = streaming;
        _peerRef = ChannelPeerLogRef.Format(ChannelId.Telegram, externalPeerId ?? "");
        _typingInterval = typingInterval ?? TypingLoop.Interval;
        _partGap = partGap ?? TelegramTextSplitter.Gap;
        if (long.TryParse(externalPeerId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
        {
            _chatId = chatId;
        }
    }

    public async ValueTask OnTurnStartedAsync(Guid turnId, CancellationToken ct)
    {
        if (_streaming is { Enabled: true } streaming && _chatId is long chatId)
        {
            _draft = await DraftStreamer.TryStartAsync(
                _client,
                chatId,
                streaming.Registry,
                streaming.PeerId,
                streaming.UserId,
                streaming.Clock,
                streaming.UpdateInterval,
                OnDraftFailedAsync,
                _logger,
                _peerRef,
                ct).ConfigureAwait(false);
            if (_draft is not null)
            {
                return;
            }
        }

        await StartTypingAsync(ct).ConfigureAwait(false);
    }

    public ValueTask OnTranscriptionAsync(string text, CancellationToken ct)
        => SendHtmlAsync(TelegramReplies.TranscriptionEcho(text), "🎙 «" + text + "»", ct);

    public ValueTask OnTextDeltaAsync(string text, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _accumulated.Append(text);
            _draft?.Update(_accumulated.ToString());
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnToolActivityAsync(string toolName, bool completed, CancellationToken ct)
        => _draft is { IsActive: true } ? ValueTask.CompletedTask : SendTypingAsync(ct);

    public async ValueTask OnConfirmationRequestedAsync(ChannelConfirmationPrompt prompt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _requestedConfirmation = true;
        if (_chatId is not long chatId || string.IsNullOrWhiteSpace(prompt.Prompt))
        {
            return;
        }

        try
        {
            var messageId = await TelegramHtmlMessage.SendAsync(
                _client,
                chatId,
                prompt.Prompt,
                TelegramConfirmationRenderer.ConfirmationButtons(prompt.ConfirmationId),
                silent: false,
                retryOnRateLimit: true,
                _partGap,
                ct).ConfigureAwait(false);
            if (messageId is long id)
            {
                _confirmations?.Remember(new TelegramConfirmationCard(
                    prompt.ConfirmationId,
                    chatId,
                    id,
                    prompt.Prompt,
                    ConfirmationClose.Open));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramTurnLog.SendFailed(_logger, exception.GetType().Name, _peerRef);
        }
    }

    public async ValueTask OnTurnFinishedAsync(ChannelTurnResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        await StopDraftAsync().ConfigureAwait(false);
        await StopTypingAsync().ConfigureAwait(false);

        if (result.Outcome is ChannelTurnOutcome.Failed or ChannelTurnOutcome.Rejected)
        {
            await SendPlainAsync(
                TelegramReplies.ForTurnError(result.ErrorCode) ?? TelegramReplies.ModelUnavailable,
                ct).ConfigureAwait(false);
            return;
        }

        if (result.Outcome == ChannelTurnOutcome.Interrupted)
        {
            await SendMarkdownAsync(StoppedText(result), ct).ConfigureAwait(false);
            return;
        }

        var text = StripCues(result.FullText);
        if (!string.IsNullOrWhiteSpace(text))
        {
            await SendMarkdownAsync(text, ct).ConfigureAwait(false);
        }

        await CloseOpenConfirmationAsync(result, ct).ConfigureAwait(false);
    }

    private async ValueTask CloseOpenConfirmationAsync(ChannelTurnResult result, CancellationToken ct)
    {
        if (_confirmations is null
            || _chatId is not long chatId
            || result.Outcome != ChannelTurnOutcome.Completed
            || _requestedConfirmation
            || !_confirmations.TryTake(chatId, out var card))
        {
            return;
        }

        try
        {
            await TelegramMarkup.ClearAsync(_client, chatId, card.MessageId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramTurnLog.SendFailed(_logger, exception.GetType().Name, _peerRef);
        }

        if (!string.IsNullOrWhiteSpace(result.FullText))
        {
            return;
        }

        var sentence = card.Close switch
        {
            ConfirmationClose.Denied => TelegramReplies.ConfirmationDenied,
            ConfirmationClose.Approved => null,
            _ => TelegramReplies.ConfirmationExpired,
        };
        if (sentence is not null)
        {
            await SendPlainAsync(sentence, ct).ConfigureAwait(false);
        }
    }

    private async Task StopDraftAsync()
    {
        var draft = _draft;
        _draft = null;
        if (draft is null)
        {
            return;
        }

        await draft.StopAsync().ConfigureAwait(false);
    }

    private ValueTask OnDraftFailedAsync(CancellationToken ct)
        => StartTypingAsync(ct);

    private ValueTask StartTypingAsync(CancellationToken ct)
    {
        if (_typing is not null)
        {
            return ValueTask.CompletedTask;
        }

        return StartTypingCoreAsync(ct);
    }

    private async ValueTask StartTypingCoreAsync(CancellationToken ct)
    {
        await SendTypingAsync(ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested || _typing is not null)
        {
            return;
        }

        _typing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _typingTask = TypingLoop.RunAsync(SendTypingAsync, _typingInterval, _typing.Token);
    }

    private string StoppedText(ChannelTurnResult result)
    {
        var partial = StripCues(_accumulated.ToString());
        if (string.IsNullOrWhiteSpace(partial))
        {
            partial = StripCues(result.FullText);
        }

        return string.IsNullOrWhiteSpace(partial)
            ? "Detenido."
            : partial.TrimEnd() + "\n— detenido";
    }

    private async ValueTask StopTypingAsync()
    {
        if (_typing is null)
        {
            return;
        }

        await _typing.CancelAsync().ConfigureAwait(false);
        var task = _typingTask;
        _typing.Dispose();
        _typing = null;
        _typingTask = null;
        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the loop is cancelled.
            }
        }
    }

    private async ValueTask SendMarkdownAsync(string markdown, CancellationToken ct)
    {
        if (_chatId is not long chatId)
        {
            return;
        }

        try
        {
            await SendMarkdownCoreAsync(chatId, markdown, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramTurnLog.SendFailed(_logger, exception.GetType().Name, _peerRef);
        }
    }

    private async Task SendMarkdownCoreAsync(long chatId, string markdown, CancellationToken ct)
    {
        if (_useRichMessages && await TrySendRichAsync(chatId, markdown, ct).ConfigureAwait(false))
        {
            return;
        }

        await SendHtmlCoreAsync(chatId, markdown, ct).ConfigureAwait(false);
    }

    private async Task<bool> TrySendRichAsync(long chatId, string markdown, CancellationToken ct)
    {
        var sanitized = TelegramRichMarkdownSanitizer.Sanitize(markdown);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return true;
        }

        var parts = TelegramTextSplitter.Split(sanitized, TelegramTextSplitter.RichMaxLength);
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0)
            {
                await Task.Delay(_partGap, ct).ConfigureAwait(false);
            }

            try
            {
                await SendRichOnceAsync(chatId, parts[index], ct).ConfigureAwait(false);
            }
            catch (TelegramApiException exception) when (exception.ErrorCode == 400 && index == 0)
            {
                TelegramTurnLog.RichMessageRejected(_logger, exception.ErrorCode.Value, _peerRef);
                return false;
            }
        }

        return true;
    }

    private async Task SendRichOnceAsync(long chatId, string markdown, CancellationToken ct)
    {
        var retried = false;
        while (true)
        {
            try
            {
                await _client.SendRichMessageAsync(
                    new SendRichMessageRequest
                    {
                        ChatId = chatId,
                        RichMessage = new InputRichMessage { Markdown = markdown },
                    },
                    ct).ConfigureAwait(false);
                return;
            }
            catch (TelegramApiException exception) when (exception.ErrorCode == 429 && !retried)
            {
                retried = true;
                await Task.Delay(exception.RetryAfter ?? TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task SendHtmlCoreAsync(long chatId, string markdown, CancellationToken ct)
    {
        var parts = TelegramTextSplitter.Split(markdown);
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0)
            {
                await Task.Delay(_partGap, ct).ConfigureAwait(false);
            }

            var part = parts[index];
            var html = TelegramMarkdownToHtml.ToHtml(part);
            if (html.Length > 4096)
            {
                await SendOnceAsync(chatId, part, parseMode: null, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                await SendOnceAsync(chatId, html, "HTML", ct).ConfigureAwait(false);
            }
            catch (TelegramApiException exception) when (IsParseError(exception))
            {
                await SendOnceAsync(chatId, part, parseMode: null, ct).ConfigureAwait(false);
            }
        }
    }

    private ValueTask SendPlainAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || _chatId is not long chatId)
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(SendGuardedAsync(chatId, text, parseMode: null, ct));
    }

    private ValueTask SendHtmlAsync(string html, string plainFallback, CancellationToken ct)
    {
        if (_chatId is not long chatId || string.IsNullOrWhiteSpace(html))
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(SendHtmlGuardedAsync(chatId, html, plainFallback, ct));
    }

    private async Task SendHtmlGuardedAsync(long chatId, string html, string plainFallback, CancellationToken ct)
    {
        try
        {
            await SendOnceAsync(chatId, html, "HTML", ct).ConfigureAwait(false);
        }
        catch (TelegramApiException exception) when (IsParseError(exception))
        {
            await SendOnceAsync(chatId, plainFallback, parseMode: null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramTurnLog.SendFailed(_logger, exception.GetType().Name, _peerRef);
        }
    }

    private async Task SendGuardedAsync(long chatId, string text, string? parseMode, CancellationToken ct)
    {
        try
        {
            await SendOnceAsync(chatId, text, parseMode, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramTurnLog.SendFailed(_logger, exception.GetType().Name, _peerRef);
        }
    }

    private async Task SendOnceAsync(long chatId, string text, string? parseMode, CancellationToken ct)
    {
        var retried = false;
        while (true)
        {
            try
            {
                await _client.SendMessageAsync(
                    new SendMessageRequest
                    {
                        ChatId = chatId,
                        Text = text,
                        ParseMode = parseMode,
                    },
                    ct).ConfigureAwait(false);
                return;
            }
            catch (TelegramApiException exception) when (exception.ErrorCode == 429 && !retried)
            {
                retried = true;
                await Task.Delay(exception.RetryAfter ?? TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask SendTypingAsync(CancellationToken ct)
    {
        if (_chatId is not long chatId)
        {
            return;
        }

        try
        {
            await _client.SendChatActionAsync(
                new SendChatActionRequest { ChatId = chatId, Action = "typing" },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TelegramTurnLog.TypingFailed(_logger, exception.GetType().Name, _peerRef);
        }
    }

    private static bool IsParseError(TelegramApiException exception)
        => exception.ErrorCode == 400
           && (exception.Description ?? "").Contains("parse entities", StringComparison.OrdinalIgnoreCase);

    private static string? StripCues(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var stripped = ExtraSpaces().Replace(CuePattern().Replace(text, " "), " ").Trim();
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped;
    }

    [GeneratedRegex(@"\[\[cue:[^\]]*\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex CuePattern();

    [GeneratedRegex(@" {2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExtraSpaces();
}
