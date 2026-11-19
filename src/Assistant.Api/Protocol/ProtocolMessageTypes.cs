namespace Assistant.Api.Protocol;

internal static class ProtocolMessageTypes
{
    public const string UserMessage = "user.message";
    public const string UserInterrupt = "user.interrupt";
    public const string UserConfirmationResponse = "user.confirmation_response";
    public const string UserAudioEnd = "user.audio_end";
    public const string NotificationAck = "user.notification_ack";
    public const string NotificationAction = "user.notification_action";

    public const string TurnStarted = "assistant.turn_started";
    public const string Token = "assistant.token";
    public const string TurnCompleted = "assistant.turn_completed";
    public const string ConfirmationRequest = "assistant.confirmation_request";
    public const string Transcription = "assistant.transcription";
    public const string AudioChunk = "assistant.audio_chunk";
    public const string Error = "assistant.error";
    public const string Notification = "assistant.notification";
    public const string ToolInvoked = "tool.invoked";
    public const string ToolCompleted = "tool.completed";

    public const string AvatarState = "avatar.state";
    public const string AvatarExpression = "avatar.expression";
    public const string AvatarGesture = "avatar.gesture";
}

internal static class ProtocolErrorCodes
{
    public const string UnsupportedMessageType = "unsupported_message_type";
    public const string UnsupportedProtocolVersion = "unsupported_protocol_version";
    public const string InvalidPayload = "invalid_payload";
    public const string ConfirmationPending = "confirmation_pending";
    public const string VoiceUnavailable = "voice_unavailable";
    public const string SpeechUnrecognized = "speech_unrecognized";
    public const string SttUnavailable = "stt_unavailable";
    public const string SttInProgress = "stt_in_progress";
}
