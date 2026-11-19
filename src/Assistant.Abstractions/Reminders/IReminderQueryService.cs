namespace Assistant.Abstractions.Reminders;

/// <summary>
/// Reads reminders for the Telegram <c>/recordatorios</c> command, the list tool, and the web.
/// <see cref="ActiveReminder.ShortRef"/> and <see cref="ReminderListItem.ShortRef"/> are the
/// 1-based position among active reminders ordered by the next occurrence, recomputed on every call.
/// </summary>
public interface IReminderQueryService
{
    /// <summary>
    /// Active reminders ordered by the next occurrence, labeled in the user's local time.
    /// </summary>
    /// <param name="userId">Owner.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active list. Empty when the user has none.</returns>
    public ValueTask<IReadOnlyList<ActiveReminder>> ListActiveAsync(Guid userId, CancellationToken ct);

    /// <summary>Lists reminders for one user.</summary>
    /// <param name="userId">Owner.</param>
    /// <param name="scope">Which statuses to include.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, active rows first.</returns>
    public ValueTask<IReadOnlyList<ReminderListItem>> ListAsync(
        Guid userId, ReminderListScope scope, int limit, CancellationToken ct);

    /// <summary>Resolves a 1-based short ref against the current active list.</summary>
    /// <param name="userId">Owner.</param>
    /// <param name="shortRef">Position, starting at 1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reminder id, or null when the position does not exist.</returns>
    public ValueTask<Guid?> FindActiveIdByShortRefAsync(
        Guid userId, int shortRef, CancellationToken ct);
}

/// <summary>One active reminder, already labeled in the user's local time.</summary>
/// <param name="ShortRef">1-based position. «Cancelá el 2» uses this number.</param>
/// <param name="Text">What to remind.</param>
/// <param name="LocalDueLabel">Local time already formatted, for example <c>hoy 21:00</c>.</param>
public sealed record ActiveReminder(int ShortRef, string Text, string LocalDueLabel);
