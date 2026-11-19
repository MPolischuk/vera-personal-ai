namespace Assistant.Channels.Telegram.Api;

/// <summary>
/// Failure talking to the Bot API. The message never includes the request URI or the bot token.
/// </summary>
public sealed class TelegramApiException : Exception
{
    public TelegramApiException(string message, int? errorCode = null, string? description = null, TimeSpan? retryAfter = null)
        : base(message)
    {
        ErrorCode = errorCode;
        Description = description;
        RetryAfter = retryAfter;
    }

    public int? ErrorCode { get; }

    /// <summary>Bot API <c>description</c>, when the body had one. Not included in <see cref="Exception.Message"/>.</summary>
    public string? Description { get; }

    public TimeSpan? RetryAfter { get; }
}
