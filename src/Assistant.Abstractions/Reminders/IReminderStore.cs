namespace Assistant.Abstractions.Reminders;

/// <summary>
/// Writes reminders. Creating a row and its <c>reminder.fire</c> job is one transaction
/// on the shared scoped database context.
/// </summary>
public interface IReminderStore
{
    /// <summary>
    /// Inserts an active reminder and schedules the first fire. Rolls both back when either write fails.
    /// </summary>
    /// <param name="reminder">Validated reminder.</param>
    /// <param name="maxActive">Maximum active reminders for the user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created row and its short ref, or a rejection.</returns>
    public ValueTask<CreateReminderResult> CreateAsync(
        NewReminder reminder, int maxActive, CancellationToken ct);

    /// <summary>
    /// Cancels the whole reminder and its pending fire job.
    /// </summary>
    /// <param name="userId">Owner.</param>
    /// <param name="reminderId">Reminder id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cancelled row, or a rejection.</returns>
    public ValueTask<CancelReminderResult> CancelAsync(
        Guid userId, Guid reminderId, CancellationToken ct);
}
