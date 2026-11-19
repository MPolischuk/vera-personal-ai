namespace Assistant.Extensions.Reminders.Time;

/// <summary>
/// Next civil occurrence of a reminder. Arithmetic stays in local time; the caller converts to UTC.
/// A missing day 29–31 falls on the last day of that month.
/// </summary>
internal static class RecurrenceCalculator
{
    private const int SafetyCap = 5_000;

    /// <summary>
    /// The next occurrence strictly after <paramref name="afterLocal"/>.
    /// Occurrences already fired are skipped from the start of the series.
    /// </summary>
    /// <param name="localStart">First civil instant the series is anchored to.</param>
    /// <param name="spec">Validated rule.</param>
    /// <param name="afterLocal">Exclusive lower bound, civil time.</param>
    /// <param name="occurrencesFired">How many occurrences from the start have already fired.</param>
    /// <returns>The next civil instant, or null when <c>COUNT</c> or <c>UNTIL</c> is exhausted.</returns>
    public static DateTime? NextLocal(
        DateTime localStart,
        RecurrenceSpec spec,
        DateTime afterLocal,
        int occurrencesFired)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var start = DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified);
        var after = DateTime.SpecifyKind(afterLocal, DateTimeKind.Unspecified);
        var index = 0;

        foreach (var occurrence in Enumerate(start, spec))
        {
            index++;
            if (index <= occurrencesFired)
            {
                continue;
            }

            if (spec.Count is int count && index > count)
            {
                return null;
            }

            if (spec.UntilLocalDate is DateOnly until
                && occurrence > until.ToDateTime(new TimeOnly(23, 59, 59)))
            {
                return null;
            }

            if (occurrence > after)
            {
                return occurrence;
            }

            if (index >= SafetyCap)
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<DateTime> Enumerate(DateTime start, RecurrenceSpec spec)
    {
        switch (spec.Frequency)
        {
            case RecurrenceFrequency.Daily:
                for (var i = 0; i < SafetyCap; i++)
                {
                    yield return start.AddDays(i * spec.Interval);
                }

                yield break;
            case RecurrenceFrequency.Weekly:
                foreach (var occurrence in Weekly(start, spec))
                {
                    yield return occurrence;
                }

                yield break;
            case RecurrenceFrequency.Monthly:
                for (var i = 0; i < SafetyCap; i++)
                {
                    var shifted = start.AddMonths(i * spec.Interval);
                    var day = Math.Min(start.Day, DateTime.DaysInMonth(shifted.Year, shifted.Month));
                    yield return new DateTime(
                        shifted.Year,
                        shifted.Month,
                        day,
                        start.Hour,
                        start.Minute,
                        start.Second,
                        DateTimeKind.Unspecified);
                }

                yield break;
            default:
                yield break;
        }
    }

    private static IEnumerable<DateTime> Weekly(DateTime start, RecurrenceSpec spec)
    {
        var days = spec.ByWeekday.Count > 0 ? spec.ByWeekday : [start.DayOfWeek];
        var time = TimeOnly.FromDateTime(start);
        var week0 = StartOfWeek(start.Date);
        var cursor = start.Date;
        var last = start.Date.AddYears(30);
        var produced = 0;

        while (cursor <= last && produced < SafetyCap)
        {
            var weekIndex = (int)((cursor - week0).TotalDays / 7);
            if (weekIndex % spec.Interval == 0 && days.Contains(cursor.DayOfWeek))
            {
                var occurrence = DateTime.SpecifyKind(cursor.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
                if (occurrence >= start)
                {
                    produced++;
                    yield return occurrence;
                }
            }

            cursor = cursor.AddDays(1);
        }
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff);
    }
}
