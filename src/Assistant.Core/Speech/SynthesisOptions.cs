namespace Assistant.Core.Speech;

public sealed record SynthesisOptions(
    SpeechCallContext Context,
    string VoiceId,
    double Speed,
    string Language);
