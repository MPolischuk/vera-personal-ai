using Microsoft.Extensions.Logging;

namespace Assistant.Core;

internal static partial class CoreLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Contributor {Contributor} failed and was skipped")]
    public static partial void ContributorFailed(ILogger logger, Exception exception, string contributor);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Tool {Tool} from {Extension} does not declare a permission level and is denied")]
    public static partial void ToolMissingLevel(ILogger logger, string tool, string extension);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Tool {Tool} failed")]
    public static partial void ToolFailed(ILogger logger, Exception exception, string tool);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Subscriber {Subscriber} failed while processing {Event}")]
    public static partial void SubscriberFailed(
        ILogger logger, Exception exception, string subscriber, string @event);
}
