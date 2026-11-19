using System.Text;
using Assistant.Abstractions.Channels;
using Assistant.Core.Conversations;

namespace Assistant.Api.Channels;

/// <summary>
/// Maps orchestrator updates to the neutral <see cref="ITurnSink"/>. Character cues are dropped.
/// Exactly one <see cref="ITurnSink.OnTurnFinishedAsync"/> is attempted.
/// </summary>
internal sealed class ChannelTurnSinkAdapter(ITurnSink sink) : IInternalTurnSink
{
    private readonly StringBuilder _text = new();
    private bool _finished;

    public async ValueTask HandleAsync(TurnUpdate update, TurnRequest request, CancellationToken ct)
    {
        switch (update)
        {
            case TurnAccepted accepted:
                await sink.OnTurnStartedAsync(accepted.TurnId, ct);
                break;

            case TurnToken token:
                _text.Append(token.Text);
                await sink.OnTextDeltaAsync(token.Text, ct);
                break;

            case TurnCharacterCue:
                break;

            case TurnToolStarted started:
                await sink.OnToolActivityAsync(started.ToolName, completed: false, ct);
                break;

            case TurnToolCompleted completed:
                await sink.OnToolActivityAsync(completed.ToolName, completed: true, ct);
                break;

            case TurnConfirmationRequired prompt:
                await sink.OnConfirmationRequestedAsync(
                    new ChannelConfirmationPrompt(
                        prompt.ConfirmationId,
                        prompt.ToolName,
                        prompt.Prompt,
                        prompt.ExpiresAt),
                    ct);
                break;

            case TurnFinished finished:
                await FinishAsync(Map(finished.Outcome), finished.ErrorCode, finished.MessageId, ct);
                break;
        }
    }

    public ValueTask CompleteAsync(CancellationToken ct)
    {
        if (_finished)
        {
            return ValueTask.CompletedTask;
        }

        return FinishAsync(ChannelTurnOutcome.Failed, "turn_failed", messageId: null, ct);
    }

    private async ValueTask FinishAsync(
        ChannelTurnOutcome outcome, string? errorCode, Guid? messageId, CancellationToken ct)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        await sink.OnTurnFinishedAsync(
            new ChannelTurnResult(
                outcome,
                _text.Length == 0 ? null : _text.ToString(),
                errorCode,
                messageId),
            ct);
    }

    private static ChannelTurnOutcome Map(TurnOutcome outcome)
        => outcome switch
        {
            TurnOutcome.Completed => ChannelTurnOutcome.Completed,
            TurnOutcome.AwaitingConfirmation => ChannelTurnOutcome.AwaitingConfirmation,
            TurnOutcome.Interrupted => ChannelTurnOutcome.Interrupted,
            TurnOutcome.Failed => ChannelTurnOutcome.Failed,
            _ => ChannelTurnOutcome.Failed,
        };
}
