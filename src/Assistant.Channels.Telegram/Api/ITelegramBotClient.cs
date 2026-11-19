using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Api;

/// <summary>Thin Bot API client. Routes do not contain the token; the redacting handler inserts it.</summary>
public interface ITelegramBotClient
{
    public Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken);

    public Task<TelegramMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken);

    public Task<TelegramMessage> SendRichMessageAsync(SendRichMessageRequest request, CancellationToken cancellationToken);

    public Task SendRichMessageDraftAsync(SendRichMessageDraftRequest request, CancellationToken cancellationToken);

    public Task<TelegramMessage> EditMessageReplyMarkupAsync(EditMessageReplyMarkupRequest request, CancellationToken cancellationToken);

    public Task AnswerCallbackQueryAsync(AnswerCallbackQueryRequest request, CancellationToken cancellationToken);

    public Task SendChatActionAsync(SendChatActionRequest request, CancellationToken cancellationToken);

    public Task<TelegramFile> GetFileAsync(string fileId, CancellationToken cancellationToken);

    public Task<Stream> DownloadFileAsync(string filePath, CancellationToken cancellationToken);

    public Task SetWebhookAsync(SetWebhookRequest request, CancellationToken cancellationToken);

    public Task<WebhookInfo> GetWebhookInfoAsync(CancellationToken cancellationToken);

    public Task DeleteWebhookAsync(bool dropPendingUpdates, CancellationToken cancellationToken);

    public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(GetUpdatesRequest request, CancellationToken cancellationToken);

    public Task SetMyCommandsAsync(IReadOnlyList<BotCommand> commands, CancellationToken cancellationToken);

    public Task<TelegramMessage> SendVoiceAsync(SendVoiceRequest request, CancellationToken cancellationToken);
}
