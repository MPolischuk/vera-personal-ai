namespace Assistant.Extensions.Reminders.Firing;

/// <summary>How late a due occurrence is when the job runs.</summary>
internal enum ReminderLateness
{
    /// <summary>Inside <c>LateGrace</c>. Deliver as written, including during quiet hours.</summary>
    OnTime = 0,

    /// <summary>Past grace and inside <c>MissedAfter</c>. Deliver with the late prefix.</summary>
    Late = 1,

    /// <summary>Past <c>MissedAfter</c>. Do not deliver this occurrence.</summary>
    Missed = 2,
}

/// <summary>Delivery choice for one occurrence.</summary>
/// <param name="Kind">On time, late, or missed.</param>
/// <param name="AllowDuringQuietHours">Explicit quiet-hours override. Only on-time reminders set it.</param>
internal readonly record struct LatenessDecision(ReminderLateness Kind, bool AllowDuringQuietHours);

/// <summary>
/// Maps lateness to a delivery choice. A negative lateness (the job ran early) is on time.
/// </summary>
internal static class LatenessPolicy
{
    public static LatenessDecision Decide(TimeSpan lateness, RemindersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (lateness <= options.LateGrace)
        {
            return new LatenessDecision(ReminderLateness.OnTime, AllowDuringQuietHours: true);
        }

        if (lateness <= options.MissedAfter)
        {
            return new LatenessDecision(ReminderLateness.Late, AllowDuringQuietHours: false);
        }

        return new LatenessDecision(ReminderLateness.Missed, AllowDuringQuietHours: false);
    }
}
