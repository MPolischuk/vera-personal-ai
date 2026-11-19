using Microsoft.Extensions.Logging;

namespace Assistant.Mcp.Coolify;

internal static partial class CoolifyLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Coolify allowlist entry is malformed. The allowlist is empty and no Coolify HTTP calls will be made.")]
    public static partial void AllowlistMalformed(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Coolify returned HTTP {StatusCode}.")]
    public static partial void HttpStatus(ILogger logger, int statusCode);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Coolify request failed.")]
    public static partial void RequestFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Coolify payload was not valid JSON.")]
    public static partial void InvalidPayload(ILogger logger, Exception exception);
}
