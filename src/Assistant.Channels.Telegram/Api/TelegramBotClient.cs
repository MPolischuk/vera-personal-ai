using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Assistant.Channels.Telegram.Api.Dto;
using Polly.Timeout;

namespace Assistant.Channels.Telegram.Api;

/// <summary>
/// Named <c>telegram</c> client. Relative paths stay free of the token.
/// </summary>
public sealed class TelegramBotClient(IHttpClientFactory httpClientFactory) : ITelegramBotClient
{
    public const string HttpClientName = "telegram";

    private static TelegramJsonContext Json => TelegramJsonContext.Default;

    public Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken)
        => SendAsync(Post("getMe"), Json.TelegramResponseTelegramUser, TelegramTimeoutHandler.MethodTimeout, cancellationToken);

    public Task<TelegramMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(
            Post("sendMessage", request, Json.SendMessageRequest),
            Json.TelegramResponseTelegramMessage,
            TelegramTimeoutHandler.MethodTimeout,
            cancellationToken);
    }

    public Task<TelegramMessage> SendRichMessageAsync(SendRichMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RichMessage);
        return SendAsync(
            Post("sendRichMessage", request, Json.SendRichMessageRequest),
            Json.TelegramResponseTelegramMessage,
            TelegramTimeoutHandler.MethodTimeout,
            cancellationToken);
    }

    public async Task SendRichMessageDraftAsync(SendRichMessageDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RichMessage);
        if (request.DraftId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "draft_id must be non-zero.");
        }

        if (!await SendAsync(
                Post("sendRichMessageDraft", request, Json.SendRichMessageDraftRequest),
                Json.TelegramResponseBoolean,
                TelegramTimeoutHandler.MethodTimeout,
                cancellationToken))
        {
            throw new TelegramApiException("Telegram sendRichMessageDraft returned false.");
        }
    }

    public Task<TelegramMessage> EditMessageReplyMarkupAsync(
        EditMessageReplyMarkupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(
            Post("editMessageReplyMarkup", request, Json.EditMessageReplyMarkupRequest),
            Json.TelegramResponseTelegramMessage,
            TelegramTimeoutHandler.MethodTimeout,
            cancellationToken);
    }

    public async Task AnswerCallbackQueryAsync(AnswerCallbackQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await SendAsync(
                Post("answerCallbackQuery", request, Json.AnswerCallbackQueryRequest),
                Json.TelegramResponseBoolean,
                TelegramTimeoutHandler.MethodTimeout,
                cancellationToken))
        {
            throw new TelegramApiException("Telegram answerCallbackQuery returned false.");
        }
    }

    public async Task SendChatActionAsync(SendChatActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await SendAsync(
                Post("sendChatAction", request, Json.SendChatActionRequest),
                Json.TelegramResponseBoolean,
                TelegramTimeoutHandler.MethodTimeout,
                cancellationToken))
        {
            throw new TelegramApiException("Telegram sendChatAction returned false.");
        }
    }

    public Task<TelegramFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return SendAsync(
            Post("getFile", new GetFileRequest { FileId = fileId }, Json.GetFileRequest),
            Json.TelegramResponseTelegramFile,
            TelegramTimeoutHandler.MethodTimeout,
            cancellationToken);
    }

    public async Task<Stream> DownloadFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!IsSafeFilePath(filePath))
        {
            throw new ArgumentException("The Telegram file path is invalid.", nameof(filePath));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "file/" + filePath);
        request.Options.Set(TelegramTimeoutHandler.TimeoutKey, TelegramTimeoutHandler.DownloadTimeout);
        using var response = await SendRawAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ReadFailureAsync(response, cancellationToken);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new MemoryStream(bytes, writable: false);
    }

    public async Task SetWebhookAsync(SetWebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await SendAsync(
                Post("setWebhook", request, Json.SetWebhookRequest),
                Json.TelegramResponseBoolean,
                TelegramTimeoutHandler.MethodTimeout,
                cancellationToken))
        {
            throw new TelegramApiException("Telegram setWebhook returned false.");
        }
    }

    public Task<WebhookInfo> GetWebhookInfoAsync(CancellationToken cancellationToken)
        => SendAsync(
            Post("getWebhookInfo"),
            Json.TelegramResponseWebhookInfo,
            TelegramTimeoutHandler.MethodTimeout,
            cancellationToken);

    public async Task DeleteWebhookAsync(bool dropPendingUpdates, CancellationToken cancellationToken)
    {
        if (!await SendAsync(
                Post("deleteWebhook", new DeleteWebhookRequest { DropPendingUpdates = dropPendingUpdates }, Json.DeleteWebhookRequest),
                Json.TelegramResponseBoolean,
                TelegramTimeoutHandler.MethodTimeout,
                cancellationToken))
        {
            throw new TelegramApiException("Telegram deleteWebhook returned false.");
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        GetUpdatesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var updates = await SendAsync(
            Post("getUpdates", request, Json.GetUpdatesRequest),
            Json.TelegramResponseListTelegramUpdate,
            TelegramTimeoutHandler.GetUpdatesTimeout,
            cancellationToken);
        return updates;
    }

    public async Task SetMyCommandsAsync(IReadOnlyList<BotCommand> commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (!await SendAsync(
                Post("setMyCommands", new SetMyCommandsRequest { Commands = commands }, Json.SetMyCommandsRequest),
                Json.TelegramResponseBoolean,
                TelegramTimeoutHandler.MethodTimeout,
                cancellationToken))
        {
            throw new TelegramApiException("Telegram setMyCommands returned false.");
        }
    }

    public Task<TelegramMessage> SendVoiceAsync(SendVoiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Voice);

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(request.ChatId.ToString(CultureInfo.InvariantCulture)), "chat_id");
        var voice = new StreamContent(request.Voice);
        voice.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType);
        content.Add(voice, "voice", request.FileName);
        if (request.Caption is not null)
        {
            content.Add(new StringContent(request.Caption), "caption");
        }

        if (request.DisableNotification is { } silent)
        {
            content.Add(new StringContent(silent ? "true" : "false"), "disable_notification");
        }

        var message = new HttpRequestMessage(HttpMethod.Post, "sendVoice") { Content = content };
        return SendAsync(message, Json.TelegramResponseTelegramMessage, TelegramTimeoutHandler.MethodTimeout, cancellationToken);
    }

    private HttpClient CreateClient() => httpClientFactory.CreateClient(HttpClientName);

    private static HttpRequestMessage Post(string method) => new(HttpMethod.Post, method);

    private static HttpRequestMessage Post<T>(string method, T body, JsonTypeInfo<T> typeInfo)
        => new(HttpMethod.Post, method)
        {
            Content = JsonContent.Create(body, typeInfo),
        };

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<TelegramResponse<T>> responseType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            request.Options.Set(TelegramTimeoutHandler.TimeoutKey, timeout);
            using var response = await SendRawAsync(request, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            TelegramResponse<T>? payload;
            try
            {
                payload = JsonSerializer.Deserialize(bytes, responseType);
            }
            catch (JsonException)
            {
                throw new TelegramApiException("Telegram response was not valid JSON.");
            }

            if (payload is not { Ok: true })
            {
                throw Failure(payload, response.StatusCode);
            }

            if (payload.Result is null)
            {
                throw new TelegramApiException("Telegram response did not include a result.");
            }

            return payload.Result;
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateClient().SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            throw new TelegramApiException("Telegram request failed before a response.");
        }
    }

    private static async Task<TelegramApiException> ReadFailureAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.Deserialize(bytes, Json.TelegramResponseBoolean);
            return Failure(payload, response.StatusCode);
        }
        catch (JsonException)
        {
            return new TelegramApiException(
                $"Telegram API error {(int)response.StatusCode}.",
                (int)response.StatusCode);
        }
    }

    private static TelegramApiException Failure<T>(TelegramResponse<T>? payload, System.Net.HttpStatusCode statusCode)
    {
        var errorCode = payload?.ErrorCode ?? (int)statusCode;
        TimeSpan? retryAfter = payload?.Parameters?.RetryAfter is int seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
        return new TelegramApiException(
            $"Telegram API error {errorCode}.",
            errorCode,
            payload?.Description,
            retryAfter);
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is HttpRequestException or TimeoutException or TimeoutRejectedException;
    }

    private static bool IsSafeFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)
            || filePath.Contains("..", StringComparison.Ordinal)
            || filePath.Contains('?', StringComparison.Ordinal)
            || filePath.Contains('#', StringComparison.Ordinal)
            || filePath.StartsWith('/')
            || filePath.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
