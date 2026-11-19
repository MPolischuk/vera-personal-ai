using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Speech;

internal static partial class SpeechLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Speech usage recorded: {Provider}/{Model} kind={Kind} sttSeconds={SttSeconds} ttsCharacters={TtsCharacters} cost={Cost}")]
    public static partial void UsageRecorded(
        ILogger logger,
        string provider,
        string model,
        string kind,
        double sttSeconds,
        int ttsCharacters,
        decimal cost);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Failed to publish speech usage for {Provider}/{Model}")]
    public static partial void UsagePublishFailed(
        ILogger logger, Exception exception, string provider, string model);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Deepgram WebSocket closed with status {Status}: {Description}")]
    public static partial void DeepgramSocketClosed(
        ILogger logger, string status, string description);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "ElevenLabs TTS request failed with status {StatusCode} against {Endpoint}")]
    public static partial void ElevenLabsRequestFailed(
        ILogger logger, int statusCode, string endpoint);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Deepgram prerecorded listen failed; falling back to streaming finals")]
    public static partial void DeepgramPrerecordedFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "ElevenLabs Realtime WebSocket closed with status {Status}: {Description}")]
    public static partial void RealtimeSocketClosed(
        ILogger logger, string status, string description);
}
