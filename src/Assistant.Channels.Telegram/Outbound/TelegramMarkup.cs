using Assistant.Channels.Telegram.Api;
using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Outbound;

/// <summary>Removes an inline keyboard. A "message is not modified" reply is success.</summary>
internal static class TelegramMarkup
{
    public static async Task ClearAsync(
        ITelegramBotClient client, long chatId, long messageId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            await client.EditMessageReplyMarkupAsync(
                new EditMessageReplyMarkupRequest
                {
                    ChatId = chatId,
                    MessageId = messageId,
                    ReplyMarkup = new InlineKeyboardMarkup { InlineKeyboard = [] },
                },
                ct).ConfigureAwait(false);
        }
        catch (TelegramApiException exception) when (IsNotModified(exception))
        {
            // The keyboard was already gone.
        }
    }

    private static bool IsNotModified(TelegramApiException exception)
        => exception.ErrorCode == 400
           && (exception.Description ?? "").Contains("not modified", StringComparison.OrdinalIgnoreCase);
}
