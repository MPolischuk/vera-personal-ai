namespace Assistant.Core.Confirmations;

/// <summary>
/// Outcome of an atomic claim: only the winner may resume or close the turn.
/// </summary>
/// <param name="Won">
/// <see langword="true"/> when this caller set <c>resolved_at</c>; otherwise another
/// path already resolved the row, ownership failed, or the row was missing.
/// </param>
/// <param name="Confirmation">
/// The row after the attempt when it was found under the given ownership; otherwise
/// <see langword="null"/>.
/// </param>
public sealed record ConfirmationClaimResult(bool Won, PendingConfirmation? Confirmation);
