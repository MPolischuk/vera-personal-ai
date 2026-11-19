using Microsoft.Extensions.Logging;

namespace Assistant.Core.Conversations;

internal static partial class TurnLog
{
    [LoggerMessage(
        EventId = 200,
        Level = LogLevel.Warning,
        Message = "Turn {TurnId} reached the tool-round cap of {Max}")]
    public static partial void ToolIterationCap(ILogger logger, Guid turnId, int max);

    [LoggerMessage(
        EventId = 201,
        Level = LogLevel.Error,
        Message = "Provider failed on turn {TurnId}")]
    public static partial void ProviderFailed(ILogger logger, Exception exception, Guid turnId);

    [LoggerMessage(
        EventId = 202,
        Level = LogLevel.Information,
        Message = "Rejected a second turn on conversation {ConversationId}")]
    public static partial void TurnInProgress(ILogger logger, Guid conversationId);
}
