using Microsoft.Extensions.Logging;

namespace Assistant.Api;

internal static partial class ApiLog
{
    [LoggerMessage(
        EventId = 700,
        Level = LogLevel.Error,
        Message = "Detached turn runner failed for conversation {ConversationId}")]
    public static partial void TurnRunnerFailed(
        ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(
        EventId = 701,
        Level = LogLevel.Warning,
        Message = "Failed to send turn {TurnId} update to connection {ConnectionId}")]
    public static partial void SendFailed(
        ILogger logger, Exception exception, Guid turnId, string connectionId);

    [LoggerMessage(
        EventId = 702,
        Level = LogLevel.Debug,
        Message = "Ignored interrupt for unknown or foreign turn {TurnId}")]
    public static partial void InterruptIgnored(ILogger logger, Guid turnId);

    [LoggerMessage(
        EventId = 703,
        Level = LogLevel.Error,
        Message = "Confirmation expiry batch failed")]
    public static partial void ConfirmationExpiryFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 704,
        Level = LogLevel.Warning,
        Message = "TTS failed for turn {TurnId}; text turn continues")]
    public static partial void TtsFailed(ILogger logger, Exception exception, Guid turnId);

    [LoggerMessage(
        EventId = 705,
        Level = LogLevel.Warning,
        Message = "STT failed for conversation {ConversationId}")]
    public static partial void SttFailed(ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(
        EventId = 706,
        Level = LogLevel.Information,
        Message =
            "Voice turn latency turnId={TurnId} f1_ms={F1Ms} f2_ms={F2Ms} first_sentence_wait_ms={FirstSentenceWaitMs} f3_ms={F3Ms} f4_ms={F4Ms}")]
    public static partial void VoiceTurnLatency(
        ILogger logger,
        Guid turnId,
        double f1Ms,
        double f2Ms,
        double firstSentenceWaitMs,
        double f3Ms,
        double f4Ms);

    [LoggerMessage(
        EventId = 707,
        Level = LogLevel.Information,
        Message = "STT audio dump written to {Path} ({Seconds:F2}s), transcript: {Transcript}")]
    public static partial void SttAudioDumped(
        ILogger logger, string path, double seconds, string transcript);

    [LoggerMessage(
        EventId = 708,
        Level = LogLevel.Warning,
        Message = "STT audio dump to {Directory} failed")]
    public static partial void SttAudioDumpFailed(
        ILogger logger, Exception exception, string directory);

    [LoggerMessage(
        EventId = 709,
        Level = LogLevel.Warning,
        Message = "Channel peer lookup failed for conversation {ConversationId}")]
    public static partial void ChannelPeerLookupFailed(
        ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(
        EventId = 710,
        Level = LogLevel.Information,
        Message = "Channel turn accepted on {Channel} for peer {PeerId} conversation {ConversationId}")]
    public static partial void ChannelTurnAccepted(
        ILogger logger, string channel, Guid peerId, Guid conversationId);

    [LoggerMessage(
        EventId = 711,
        Level = LogLevel.Information,
        Message = "Channel turn rejected on {Channel} for peer {PeerId}: {RejectionCode}")]
    public static partial void ChannelTurnRejected(
        ILogger logger, string channel, Guid peerId, string rejectionCode);

    [LoggerMessage(
        EventId = 712,
        Level = LogLevel.Warning,
        Message = "No turn sink factory for channel {Channel}; confirmation expiry uses the hub path")]
    public static partial void ChannelSinkFactoryMissing(ILogger logger, string channel);

    [LoggerMessage(
        EventId = 713,
        Level = LogLevel.Warning,
        Message = "Inbound audio transcription failed for peer {PeerId}")]
    public static partial void InboundAudioFailed(ILogger logger, Exception exception, Guid peerId);
}
