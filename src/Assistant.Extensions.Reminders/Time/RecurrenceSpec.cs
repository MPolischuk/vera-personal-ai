using System.Globalization;

namespace Assistant.Extensions.Reminders.Time;

/// <summary>How often a reminder repeats. The server builds the RRULE; the model never sends one.</summary>
internal enum RecurrenceFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}

/// <summary>
/// Subset of RFC 5545 stored on the row: <c>FREQ</c>, <c>INTERVAL</c>, <c>BYDAY</c> (weekly only),
/// <c>BYMONTHDAY</c> and <c>BYSETPOS</c> (monthly only, ignored when reading),
/// <c>UNTIL</c> as the end of a local date, and <c>COUNT</c>.
/// A new <c>UNTIL</c> is the end of that local day in UTC. The day of a monthly rule stays on
/// <c>LocalStart</c>; a missing day 29–31 is still the last day of the month.
/// </summary>
/// <param name="Frequency">Daily, weekly, or monthly.</param>
/// <param name="Interval">Step, from 1 to 12.</param>
/// <param name="ByWeekday">Weekdays for a weekly rule. Empty means the weekday of the start.</param>
/// <param name="UntilLocalDate">Last local date, inclusive, or null.</param>
/// <param name="Count">How many occurrences, or null.</param>
internal sealed record RecurrenceSpec(
    RecurrenceFrequency Frequency,
    int Interval,
    IReadOnlyList<DayOfWeek> ByWeekday,
    DateOnly? UntilLocalDate,
    int? Count)
{
    public string ToRule(DateTime localStart, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var parts = new List<string> { "FREQ=" + FrequencyCode(Frequency) };
        if (Interval != 1)
        {
            parts.Add(FormattableString.Invariant($"INTERVAL={Interval}"));
        }

        if (Frequency == RecurrenceFrequency.Weekly && ByWeekday.Count > 0)
        {
            parts.Add("BYDAY=" + string.Join(",", ByWeekday.Select(DayCode)));
        }

        if (Frequency == RecurrenceFrequency.Monthly)
        {
            var day = localStart.Day;
            if (day <= 28)
            {
                parts.Add(FormattableString.Invariant($"BYMONTHDAY={day}"));
            }
            else
            {
                parts.Add("BYMONTHDAY=" + string.Join(',', Enumerable.Range(28, day - 27)));
                parts.Add("BYSETPOS=-1");
            }
        }

        if (UntilLocalDate is DateOnly until)
        {
            parts.Add(FormatUntil(until, zone));
        }

        if (Count is int count)
        {
            parts.Add(FormattableString.Invariant($"COUNT={count}"));
        }

        return string.Join(';', parts);
    }

    /// <summary>
    /// Text of <see cref="ToRule"/> when a monthly rule has no <c>BYMONTHDAY</c> or an <c>UNTIL</c>
    /// has no <c>Z</c>. Returns <paramref name="rule"/> when the stored text can stay.
    /// </summary>
    public static string UpgradeStoredRule(string rule, DateTime localStart, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (string.IsNullOrWhiteSpace(rule) || !NeedsUpgrade(rule))
        {
            return rule;
        }

        if (!TryParseRule(rule, zone, out var spec) || spec is null)
        {
            return rule;
        }

        var upgraded = spec.ToRule(localStart, zone);
        return string.Equals(upgraded, rule, StringComparison.Ordinal) ? rule : upgraded;
    }

    public string Describe(DateTime localStart)
    {
        var days = ByWeekday.Count > 0 ? ByWeekday : [localStart.DayOfWeek];
        return Frequency switch
        {
            RecurrenceFrequency.Daily when Interval == 1 => "todos los días",
            RecurrenceFrequency.Daily => FormattableString.Invariant($"cada {Interval} días"),
            RecurrenceFrequency.Weekly when Interval == 1 && days.Count == 1 => Every(days[0]),
            RecurrenceFrequency.Weekly when Interval == 2 && days.Count == 1 => EveryOther(days[0]),
            RecurrenceFrequency.Weekly when days.Count == 1
                => FormattableString.Invariant($"cada {Interval} semanas, los {PluralDay(days[0])}"),
            RecurrenceFrequency.Weekly => "los " + string.Join(" y ", days.Select(PluralDay)),
            RecurrenceFrequency.Monthly when Interval == 1
                => FormattableString.Invariant($"el {localStart.Day} de cada mes"),
            RecurrenceFrequency.Monthly
                => FormattableString.Invariant($"cada {Interval} meses, el día {localStart.Day}"),
            _ => "recurrente",
        };
    }

    public static bool TryParseRule(string? rule, TimeZoneInfo zone, out RecurrenceSpec? spec)
    {
        ArgumentNullException.ThrowIfNull(zone);
        spec = null;
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        RecurrenceFrequency? frequency = null;
        var interval = 1;
        var days = new List<DayOfWeek>();
        DateOnly? until = null;
        int? count = null;

        foreach (var part in rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length != 2)
            {
                return false;
            }

            switch (split[0])
            {
                case "FREQ":
                    frequency = split[1] switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        _ => null,
                    };
                    if (frequency is null)
                    {
                        return false;
                    }

                    break;
                case "INTERVAL":
                    if (!int.TryParse(split[1], out interval))
                    {
                        return false;
                    }

                    break;
                case "BYDAY":
                    foreach (var code in split[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!TryDay(code, out var day))
                        {
                            return false;
                        }

                        days.Add(day);
                    }

                    break;
                case "BYMONTHDAY":
                    if (!IsMonthDayList(split[1]))
                    {
                        return false;
                    }

                    break;
                case "BYSETPOS":
                    if (!int.TryParse(split[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                    {
                        return false;
                    }

                    break;
                case "UNTIL":
                    if (!TryUntil(split[1], zone, out var date))
                    {
                        return false;
                    }

                    until = date;
                    break;
                case "COUNT":
                    if (!int.TryParse(split[1], out var parsed))
                    {
                        return false;
                    }

                    count = parsed;
                    break;
                default:
                    return false;
            }
        }

        if (frequency is null)
        {
            return false;
        }

        spec = new RecurrenceSpec(frequency.Value, interval, days, until, count);
        return true;
    }

    public static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        DayOfWeek.Sunday => "SU",
        _ => "MO",
    };

    public static bool TryDay(string code, out DayOfWeek day)
    {
        switch (code.ToUpperInvariant())
        {
            case "MO":
                day = DayOfWeek.Monday;
                return true;
            case "TU":
                day = DayOfWeek.Tuesday;
                return true;
            case "WE":
                day = DayOfWeek.Wednesday;
                return true;
            case "TH":
                day = DayOfWeek.Thursday;
                return true;
            case "FR":
                day = DayOfWeek.Friday;
                return true;
            case "SA":
                day = DayOfWeek.Saturday;
                return true;
            case "SU":
                day = DayOfWeek.Sunday;
                return true;
            default:
                day = default;
                return false;
        }
    }

    public static string SpanishDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miércoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sábado",
        DayOfWeek.Sunday => "domingo",
        _ => "día",
    };

    private static string PluralDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "sábados",
        DayOfWeek.Sunday => "domingos",
        _ => SpanishDay(day),
    };

    private static string Every(DayOfWeek day) => "todos los " + PluralDay(day);

    private static string EveryOther(DayOfWeek day) => "cada dos " + PluralDay(day);

    private static string FrequencyCode(RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Daily => "DAILY",
        RecurrenceFrequency.Weekly => "WEEKLY",
        RecurrenceFrequency.Monthly => "MONTHLY",
        _ => "DAILY",
    };

    private static bool NeedsUpgrade(string rule)
    {
        var monthlyWithoutDay = rule.Contains("FREQ=MONTHLY", StringComparison.Ordinal)
            && !rule.Contains("BYMONTHDAY=", StringComparison.Ordinal);
        return monthlyWithoutDay || UntilLacksUtc(rule);
    }

    private static bool UntilLacksUtc(string rule)
    {
        foreach (var part in rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("UNTIL=", StringComparison.Ordinal))
            {
                return !part.EndsWith('Z');
            }
        }

        return false;
    }

    private static string FormatUntil(DateOnly until, TimeZoneInfo zone)
    {
        var local = new DateTime(until.Year, until.Month, until.Day, 23, 59, 59, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return FormattableString.Invariant($"UNTIL={utc:yyyyMMdd}T{utc:HHmmss}Z");
    }

    private static bool TryUntil(string value, TimeZoneInfo zone, out DateOnly date)
    {
        if (value.EndsWith('Z'))
        {
            if (!DateTime.TryParseExact(
                    value,
                    "yyyyMMdd'T'HHmmss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var utc))
            {
                date = default;
                return false;
            }

            var local = TimeZoneInfo.ConvertTime(
                new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero),
                zone);
            date = DateOnly.FromDateTime(local.DateTime);
            return true;
        }

        if (value.Length >= 8
            && DateOnly.TryParseExact(
                value[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return true;
        }

        date = default;
        return false;
    }

    private static bool IsMonthDayList(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
                || day is < 1 or > 31)
            {
                return false;
            }
        }

        return true;
    }
}
