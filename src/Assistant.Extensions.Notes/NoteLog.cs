using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Notes;

internal static partial class NoteLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Note embedding failed for turn {TurnId}.")]
    public static partial void EmbeddingFailed(ILogger logger, Exception exception, Guid turnId);
}
