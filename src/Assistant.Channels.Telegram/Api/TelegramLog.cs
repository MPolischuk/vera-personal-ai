using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Api;

internal static partial class TelegramLog
{
    [LoggerMessage(
        EventId = 790,
        Level = LogLevel.Information,
        Message = "Telegram {ApiMethod} completed with {StatusCode} in {ElapsedMilliseconds} ms (ok={Ok}, error_code={ErrorCode}, path={Path})")]
    public static partial void CallCompleted(
        ILogger logger,
        string apiMethod,
        int statusCode,
        long elapsedMilliseconds,
        string ok,
        string errorCode,
        string path);

    [LoggerMessage(
        EventId = 791,
        Level = LogLevel.Warning,
        Message = "Telegram {ApiMethod} failed in {ElapsedMilliseconds} ms ({ExceptionType}, path={Path})")]
    public static partial void CallFailed(
        ILogger logger,
        string apiMethod,
        long elapsedMilliseconds,
        string exceptionType,
        string path);
}
