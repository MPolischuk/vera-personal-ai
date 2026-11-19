using System.Text.Json.Serialization;
using Assistant.Channels.Telegram.Api.Dto;

namespace Assistant.Channels.Telegram.Api;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TelegramResponse<TelegramUser>))]
[JsonSerializable(typeof(TelegramResponse<TelegramMessage>))]
[JsonSerializable(typeof(TelegramResponse<TelegramFile>))]
[JsonSerializable(typeof(TelegramResponse<WebhookInfo>))]
[JsonSerializable(typeof(TelegramResponse<List<TelegramUpdate>>))]
[JsonSerializable(typeof(TelegramResponse<bool>))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(SendRichMessageRequest))]
[JsonSerializable(typeof(SendRichMessageDraftRequest))]
[JsonSerializable(typeof(EditMessageReplyMarkupRequest))]
[JsonSerializable(typeof(AnswerCallbackQueryRequest))]
[JsonSerializable(typeof(SendChatActionRequest))]
[JsonSerializable(typeof(GetFileRequest))]
[JsonSerializable(typeof(SetWebhookRequest))]
[JsonSerializable(typeof(DeleteWebhookRequest))]
[JsonSerializable(typeof(GetUpdatesRequest))]
[JsonSerializable(typeof(SetMyCommandsRequest))]
internal sealed partial class TelegramJsonContext : JsonSerializerContext;
