using System.Globalization;

namespace Assistant.Extensions.Reminders.Time;

/// <summary>
/// Spanish phrases for the web. The list tool keeps <see cref="RecurrenceSpec.Describe"/>.
/// Hours stay on the server so the client does not convert time zones.
/// </summary>
public static class RecurrenceDescriber
{
    /// <summary>
    /// Readable recurrence, or null when the row is a single shot or the rule is not the stored subset.
    /// </summary>
    /// <param name="rule">Stored RRULE subset.</param>
    /// <param name="localStart">Civil start, without a zone.</param>
    /// <param name="zone">Zone stored on the reminder. Used to read an <c>UNTIL</c> that ends in <c>Z</c>.</param>
    /// <returns>The phrase, or null.</returns>
    public static string? Describe(string? rule, DateTime localStart, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (!RecurrenceSpec.TryParseRule(rule, zone, out var spec) || spec is null)
        {
            return null;
        }

        var days = spec.ByWeekday.Count > 0 ? spec.ByWeekday : [localStart.DayOfWeek];
        return spec.Frequency switch
        {
            RecurrenceFrequency.Daily when spec.Interval == 1 => "todos los días",
            RecurrenceFrequency.Daily => FormattableString.Invariant($"cada {spec.Interval} días"),
            RecurrenceFrequency.Weekly when spec.Interval == 1 && days.Count == 1
                => $"todos los {PluralDay(days[0])} a las {Clock(localStart)}",
            RecurrenceFrequency.Weekly when days.Count == 1
                => FormattableString.Invariant($"cada {spec.Interval} {PluralDay(days[0])}"),
            RecurrenceFrequency.Weekly => "los " + string.Join(" y ", days.Select(PluralDay)),
            RecurrenceFrequency.Monthly when spec.Interval == 1
                => FormattableString.Invariant($"el día {localStart.Day} de cada mes"),
            RecurrenceFrequency.Monthly
                => FormattableString.Invariant($"cada {spec.Interval} meses, el día {localStart.Day}"),
            _ => "recurrente",
        };
    }

    /// <summary>Local clock of a UTC instant, with a numeric offset (<c>2026-10-08T21:00:00-03:00</c>).</summary>
    /// <param name="utc">Instant.</param>
    /// <param name="zone">Zone already stored on the reminder.</param>
    /// <returns>ISO-8601 local time.</returns>
    public static string FormatOffset(DateTimeOffset utc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(utc, zone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>Civil local time with the offset of <paramref name="zone"/> at that clock.</summary>
    /// <param name="local">Civil time, without a zone.</param>
    /// <param name="zone">Zone stored on the reminder.</param>
    /// <returns>ISO-8601 local time.</returns>
    public static string FormatCivil(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        return FormatOffset(new DateTimeOffset(utc, TimeSpan.Zero), zone);
    }

    /// <summary>Civil clock of a UTC instant in <paramref name="zone"/>.</summary>
    /// <param name="utc">Instant.</param>
    /// <param name="zone">Zone.</param>
    /// <returns>Unspecified civil time.</returns>
    public static DateTime ToCivil(DateTimeOffset utc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var zoned = TimeZoneInfo.ConvertTime(utc, zone);
        return DateTime.SpecifyKind(zoned.DateTime, DateTimeKind.Unspecified);
    }

    /// <summary>Future phrases match the tool. Past phrases say how long ago.</summary>
    /// <param name="local">Civil due time.</param>
    /// <param name="nowLocal">Civil now in the same zone.</param>
    /// <returns>A relative phrase.</returns>
    public static string Relative(DateTime local, DateTime nowLocal)
    {
        if (local >= nowLocal)
        {
            return ReminderTimeText.Relative(local, nowLocal);
        }

        var ago = nowLocal - local;
        var minutes = (int)Math.Round(ago.TotalMinutes);
        if (minutes < 1)
        {
            return "hace un momento";
        }

        if (minutes < 60)
        {
            return minutes == 1 ? "hace 1 minuto" : FormattableString.Invariant($"hace {minutes} minutos");
        }

        var hours = (int)Math.Round(ago.TotalHours);
        if (hours < 36)
        {
            return hours == 1 ? "hace 1 hora" : FormattableString.Invariant($"hace {hours} horas");
        }

        var days = (int)Math.Round(ago.TotalDays);
        return days == 1 ? "hace 1 día" : FormattableString.Invariant($"hace {days} días");
    }

    private static string Clock(DateTime local)
        => local.Minute == 0
            ? local.Hour.ToString(CultureInfo.InvariantCulture)
            : local.ToString("H:mm", CultureInfo.InvariantCulture);

    private static string PluralDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "sábados",
        DayOfWeek.Sunday => "domingos",
        _ => RecurrenceSpec.SpanishDay(day),
    };
}
