namespace Assistant.Api.Protocol;

/// <summary>
/// Pure factory for avatar protocol envelopes. Invalid cues are dropped (null).
/// </summary>
internal static class AvatarCues
{
    public const string Idle = "idle";
    public const string Listening = "listening";
    public const string Thinking = "thinking";
    public const string Speaking = "speaking";

    public const string ExpressionHappy = "happy";
    public const string ExpressionAngry = "angry";
    public const string ExpressionSad = "sad";
    public const string ExpressionRelaxed = "relaxed";
    public const string ExpressionSurprised = "surprised";
    public const string ExpressionNeutral = "neutral";

    public const double ThinkingWeight = 0.4;
    public const int ThinkingDurationMs = 280;
    public const int NeutralDurationMs = 280;

    public const double CharacterExpressionWeight = 0.8;
    public const int CharacterExpressionDurationMs = 400;

    public const string GestureNod = "nod";
    public const string GestureShake = "shake";
    public const string GestureWave = "wave";
    public const string GestureShrug = "shrug";

    public const double CharacterGestureIntensity = 0.7;
    public const int CharacterGestureDurationMs = 1100;

    private static readonly HashSet<string> ValidStates = new(StringComparer.Ordinal)
    {
        Idle,
        Listening,
        Thinking,
        Speaking,
    };

    private static readonly HashSet<string> ValidExpressions = new(StringComparer.Ordinal)
    {
        ExpressionHappy,
        ExpressionAngry,
        ExpressionSad,
        ExpressionRelaxed,
        ExpressionSurprised,
        ExpressionNeutral,
    };

    private static readonly HashSet<string> ValidGestures = new(StringComparer.Ordinal)
    {
        GestureNod,
        GestureShake,
        GestureWave,
        GestureShrug,
    };

    public static bool IsValidState(string state)
        => ValidStates.Contains(state);

    public static bool IsValidExpression(string expression)
        => ValidExpressions.Contains(expression);

    public static bool IsValidGesture(string gesture)
        => ValidGestures.Contains(gesture);

    public static MessageEnvelope? State(string state, string? correlationId, TimeProvider clock)
    {
        if (!IsValidState(state))
        {
            return null;
        }

        return MessageEnvelope.Create(
            ProtocolMessageTypes.AvatarState,
            correlationId,
            new AvatarStatePayload(state),
            clock);
    }

    public static MessageEnvelope? Expression(
        string expression,
        double weight,
        int durationMs,
        string? correlationId,
        TimeProvider clock)
    {
        if (!IsValidExpression(expression))
        {
            return null;
        }

        return MessageEnvelope.Create(
            ProtocolMessageTypes.AvatarExpression,
            correlationId,
            new AvatarExpressionPayload(expression, weight, durationMs),
            clock);
    }

    public static IReadOnlyList<MessageEnvelope> ThinkingPair(string? correlationId, TimeProvider clock)
        => BuildPair(
            State(Thinking, correlationId, clock),
            Expression(ExpressionRelaxed, ThinkingWeight, ThinkingDurationMs, correlationId, clock));

    public static IReadOnlyList<MessageEnvelope> IdlePair(string? correlationId, TimeProvider clock)
        => BuildPair(
            State(Idle, correlationId, clock),
            Expression(ExpressionNeutral, weight: 0, NeutralDurationMs, correlationId, clock));

    public static MessageEnvelope? SpeakingOnce(string? correlationId, TimeProvider clock)
        => State(Speaking, correlationId, clock);

    public static MessageEnvelope? ListeningOnce(string? correlationId, TimeProvider clock)
        => State(Listening, correlationId, clock);

    /// <summary>
    /// Projects a character cue expression. <c>none</c>/<c>neutral</c>/unknown → null.
    /// Gesture is projected separately via <see cref="Gesture"/>.
    /// </summary>
    public static MessageEnvelope? FromCharacterCue(
        string expression,
        string? correlationId,
        TimeProvider clock)
    {
        if (expression is ExpressionNeutral or "none")
        {
            return null;
        }

        return Expression(
            expression,
            CharacterExpressionWeight,
            CharacterExpressionDurationMs,
            correlationId,
            clock);
    }

    /// <summary>
    /// Projects a character cue gesture. <c>none</c>/unknown → null.
    /// </summary>
    public static MessageEnvelope? Gesture(string gesture, string? correlationId, TimeProvider clock)
    {
        if (!IsValidGesture(gesture))
        {
            return null;
        }

        return MessageEnvelope.Create(
            ProtocolMessageTypes.AvatarGesture,
            correlationId,
            new AvatarGesturePayload(gesture, CharacterGestureIntensity, CharacterGestureDurationMs),
            clock);
    }

    private static IReadOnlyList<MessageEnvelope> BuildPair(
        MessageEnvelope? state,
        MessageEnvelope? expression)
    {
        if (state is null && expression is null)
        {
            return [];
        }

        if (state is null)
        {
            return [expression!];
        }

        if (expression is null)
        {
            return [state];
        }

        return [state, expression];
    }
}
