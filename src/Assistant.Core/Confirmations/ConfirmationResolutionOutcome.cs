namespace Assistant.Core.Confirmations;

/// <summary>
/// How a pending confirmation was closed. Stored as a stable string on the row
/// (<c>approved</c> / <c>denied</c> / <c>expired</c>).
/// </summary>
public enum ConfirmationResolutionOutcome
{
    Approved,
    Denied,
    Expired,
}
