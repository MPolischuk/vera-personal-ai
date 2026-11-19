using Assistant.Abstractions.Events;

namespace Assistant.Extensions.Reminders.Events;

/// <summary>
/// Fact that a reminder occurrence was committed. Published after the firing transaction.
/// The notice itself is the notification row and its dispatch job; this event does not
/// carry the reminder text.
/// </summary>
/// <param name="ReminderId">Reminder that fired.</param>
/// <param name="UserId">Owner.</param>
/// <param name="OccurrenceUtc">Occurrence the job committed, UTC.</param>
/// <param name="Notified">Whether a notification was written.</param>
/// <param name="NextOccurrenceUtc">Next occurrence, or null when the reminder reached a final status.</param>
public sealed record ReminderDue(
    Guid ReminderId,
    Guid UserId,
    DateTimeOffset OccurrenceUtc,
    bool Notified,
    DateTimeOffset? NextOccurrenceUtc) : IDomainEvent;
