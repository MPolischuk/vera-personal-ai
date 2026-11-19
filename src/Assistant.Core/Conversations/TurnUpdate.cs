using Assistant.Core.Permissions;

namespace Assistant.Core.Conversations;

/// <summary>
/// A turn stream update. Closed hierarchy on purpose: the PT-07 switch must be exhaustive (D2).
/// </summary>
public abstract record TurnUpdate
{
    private protected TurnUpdate() { }
}

public sealed record TurnAccepted(Guid TurnId) : TurnUpdate;

public sealed record TurnToken(string Text) : TurnUpdate;

/// <summary>
/// Character cue preamble stripped from the model reply (ADR-0014). Not a chat token.
/// </summary>
public sealed record TurnCharacterCue(string Expression, string Gesture) : TurnUpdate;

public sealed record TurnToolStarted(string ToolName) : TurnUpdate;

public sealed record TurnToolCompleted(string ToolName, PermissionOutcome Outcome, bool Succeeded) : TurnUpdate;

public sealed record TurnConfirmationRequired(
    Guid ConfirmationId,
    string ToolName,
    string Prompt,
    DateTimeOffset ExpiresAt) : TurnUpdate;

/// <summary>Sole terminal update. The stream always ends in exactly one.</summary>
public sealed record TurnFinished(
    TurnOutcome Outcome,
    Guid? MessageId,
    TimeSpan Duration,
    string? ErrorCode = null,
    string? ErrorMessage = null) : TurnUpdate;

public enum TurnOutcome { Completed, Interrupted, AwaitingConfirmation, Failed }

public static class TurnErrorCodes
{
    public const string TurnInProgress = "turn_in_progress";
    public const string ConversationNotFound = "conversation_not_found";
    public const string ContextBudgetExceeded = "context_budget_exceeded";
    public const string ModelUnavailable = "model_unavailable";
    public const string ConfirmationInvalid = "confirmation_invalid";
}
