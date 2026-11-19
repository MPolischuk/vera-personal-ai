namespace Assistant.Core.Speech;

public sealed record TranscriptionEvent(string Text, bool IsFinal, double? Confidence);
