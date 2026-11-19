using Microsoft.Extensions.Logging;

namespace Assistant.Channels.WebPush;

internal static partial class WebPushLog
{
    [LoggerMessage(
        EventId = 870,
        Level = LogLevel.Information,
        Message = "Web Push accepted for {EndpointHash}")]
    public static partial void Accepted(ILogger logger, string endpointHash);

    [LoggerMessage(
        EventId = 871,
        Level = LogLevel.Warning,
        Message = "Web Push delivery returned {StatusCode} for {EndpointHash}")]
    public static partial void Rejected(ILogger logger, int statusCode, string endpointHash);

    [LoggerMessage(
        EventId = 872,
        Level = LogLevel.Error,
        Message = "Web Push request failed ({Error}) for {EndpointHash}")]
    public static partial void Failed(ILogger logger, string error, string endpointHash);

    [LoggerMessage(
        EventId = 873,
        Level = LogLevel.Error,
        Message = "Web Push VAPID was rejected for every active subscription")]
    public static partial void VapidRejectedForAll(ILogger logger);

    [LoggerMessage(
        EventId = 874,
        Level = LogLevel.Error,
        Message = "Web Push rejected invalid actions")]
    public static partial void ActionsInvalid(ILogger logger);
}
