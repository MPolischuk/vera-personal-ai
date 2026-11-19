namespace Assistant.Core.Speech;

/// <summary>Which turn is charged for the cost of this speech call.</summary>
public sealed record SpeechCallContext(Guid ConversationId, Guid TurnId);
