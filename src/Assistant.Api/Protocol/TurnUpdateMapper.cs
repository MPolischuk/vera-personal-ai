using Assistant.Core.Conversations;

namespace Assistant.Api.Protocol;

internal static class TurnUpdateMapper
{
    public static MessageEnvelope ToEnvelope(
        TurnUpdate update,
        Guid conversationId,
        Guid? turnId,
        TimeProvider clock)
        => update switch
        {
            TurnAccepted accepted => MessageEnvelope.Create(
                ProtocolMessageTypes.TurnStarted,
                accepted.TurnId.ToString("D"),
                new TurnStartedPayload(accepted.TurnId, conversationId),
                clock),
            TurnToken token => MessageEnvelope.Create(
                ProtocolMessageTypes.Token,
                Correlation(turnId),
                new TokenPayload(token.Text),
                clock),
            TurnToolStarted started => MessageEnvelope.Create(
                ProtocolMessageTypes.ToolInvoked,
                Correlation(turnId),
                new ToolInvokedPayload(started.ToolName),
                clock),
            TurnToolCompleted completed => MessageEnvelope.Create(
                ProtocolMessageTypes.ToolCompleted,
                Correlation(turnId),
                new ToolCompletedPayload(
                    completed.ToolName, completed.Outcome.ToString(), completed.Succeeded),
                clock),
            TurnConfirmationRequired required => MessageEnvelope.Create(
                ProtocolMessageTypes.ConfirmationRequest,
                Correlation(turnId),
                new ConfirmationRequestPayload(
                    required.ConfirmationId, required.ToolName, required.Prompt, required.ExpiresAt),
                clock),
            TurnFinished { Outcome: TurnOutcome.Failed } failed => MessageEnvelope.Create(
                ProtocolMessageTypes.Error,
                Correlation(turnId),
                new ErrorPayload(
                    failed.ErrorCode ?? "turn_failed",
                    failed.ErrorMessage ?? "The turn failed."),
                clock),
            TurnFinished finished => MessageEnvelope.Create(
                ProtocolMessageTypes.TurnCompleted,
                Correlation(turnId),
                new TurnCompletedPayload(
                    finished.MessageId,
                    finished.Outcome.ToString(),
                    (int)finished.Duration.TotalMilliseconds),
                clock),
            TurnCharacterCue cue => throw new ArgumentOutOfRangeException(
                nameof(update),
                cue,
                "TurnCharacterCue is projected by TurnRunner."),
            _ => throw new ArgumentOutOfRangeException(nameof(update), update, "Unknown turn update."),
        };

    public static MessageEnvelope TurnStarted(
        TurnAccepted accepted, Guid conversationId, TimeProvider clock)
        => MessageEnvelope.Create(
            ProtocolMessageTypes.TurnStarted,
            accepted.TurnId.ToString("D"),
            new TurnStartedPayload(accepted.TurnId, conversationId),
            clock);

    private static string? Correlation(Guid? turnId)
        => turnId?.ToString("D");
}
