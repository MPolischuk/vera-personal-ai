using Microsoft.Extensions.Logging;

namespace Assistant.Mcp.Search;

internal static partial class SearchLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Search unavailable before the request. MissingQuery={MissingQuery} MissingOrigin={MissingOrigin}")]
    public static partial void UnavailableBeforeRequest(
        ILogger logger, bool missingQuery, bool missingOrigin);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Search unavailable. StatusCode={StatusCode}")]
    public static partial void UnavailableStatus(ILogger logger, int statusCode);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Search unavailable. The response was not a result list.")]
    public static partial void UnavailableBody(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Search unavailable. The request timed out.")]
    public static partial void UnavailableTimeout(ILogger logger);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Search unavailable. ExceptionType={ExceptionType}")]
    public static partial void UnavailableException(ILogger logger, string exceptionType);
}
