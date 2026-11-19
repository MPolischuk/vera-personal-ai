using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>
/// Sends one logical reply as HTML <c>sendMessage</c> calls, cut at 4 000 characters,
/// with a plain retry when Telegram rejects the entities.
/// </summary>
internal static class TelegramHtmlMessage
{
    public static async Task<long?> SendAsync(
        ITelegramBotClient client,
        long chatId,
        string text,
        InlineKeyboardMarkup? markup,
        bool silent,
        bool retryOnRateLimit,
        TimeSpan partGap,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(text);
        var parts = TelegramTextSplitter.Split(text);
        if (parts.Count == 0)
        {
            return null;
        }

        long? last = null;
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0)
            {
                await Task.Delay(partGap, ct).ConfigureAwait(false);
            }

            var buttons = index == parts.Count - 1 ? markup : null;
            last = await SendPartAsync(client, chatId, parts[index], buttons, silent, retryOnRateLimit, ct)
                .ConfigureAwait(false);
        }

        return last;
    }

    private static async Task<long> SendPartAsync(
        ITelegramBotClient client,
        long chatId,
        string part,
        InlineKeyboardMarkup? markup,
        bool silent,
        bool retryOnRateLimit,
        CancellationToken ct)
    {
        var html = TelegramMarkdownToHtml.ToHtml(part);
        if (html.Length > 4096)
        {
            return (await SendRawAsync(client, chatId, part, parseMode: null, markup, silent, retryOnRateLimit, ct)
                .ConfigureAwait(false)).MessageId;
        }

        try
        {
            return (await SendRawAsync(client, chatId, html, "HTML", markup, silent, retryOnRateLimit, ct)
                .ConfigureAwait(false)).MessageId;
        }
        catch (TelegramApiException exception) when (IsParseError(exception))
        {
            return (await SendRawAsync(client, chatId, part, parseMode: null, markup, silent, retryOnRateLimit, ct)
                .ConfigureAwait(false)).MessageId;
        }
    }

    private static async Task<TelegramMessage> SendRawAsync(
        ITelegramBotClient client,
        long chatId,
        string text,
        string? parseMode,
        InlineKeyboardMarkup? markup,
        bool silent,
        bool retryOnRateLimit,
        CancellationToken ct)
    {
        var retried = false;
        while (true)
        {
            try
            {
                return await client.SendMessageAsync(
                    new SendMessageRequest
                    {
                        ChatId = chatId,
                        Text = text,
                        ParseMode = parseMode,
                        DisableNotification = silent ? true : null,
                        ReplyMarkup = markup,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (TelegramApiException exception) when (retryOnRateLimit && exception.ErrorCode == 429 && !retried)
            {
                retried = true;
                await Task.Delay(exception.RetryAfter ?? TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsParseError(TelegramApiException exception)
        => exception.ErrorCode == 400
           && (exception.Description ?? "").Contains("parse entities", StringComparison.OrdinalIgnoreCase);
}
