using Assistant.Persistence.Entities;

namespace Assistant.Persistence.Reminders;

internal sealed class ReminderEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string TimeZone { get; set; } = string.Empty;

    public DateTime LocalStart { get; set; }

    public DateTimeOffset? NextOccurrenceAt { get; set; }

    public string? RecurrenceRule { get; set; }

    public int OccurrencesFired { get; set; }

    public DateTimeOffset? LastFiredAt { get; set; }

    public string OriginChannel { get; set; } = string.Empty;

    public Guid? OriginConversationId { get; set; }

    public string Provenance { get; set; } = "{}";

    public int SnoozeCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
