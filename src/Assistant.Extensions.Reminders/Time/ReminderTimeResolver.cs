using System.Globalization;
using System.Xml;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Reminders.Time;

/// <summary>What the model sent in <c>due</c>. Exactly one of the two strings is set.</summary>
/// <param name="Duration">ISO 8601 duration, or null.</param>
/// <param name="LocalDateTime">Civil date-time without a zone, or null.</param>
internal sealed record DueInput(string? Duration, string? LocalDateTime);

/// <summary>What the model sent in <c>recurrence</c>. Null when the reminder does not repeat.</summary>
/// <param name="Frequency"><c>daily</c>, <c>weekly</c>, or <c>monthly</c>.</param>
/// <param name="Interval">Step, or null for 1.</param>
/// <param name="ByWeekday">Weekday codes, or null when the field was omitted.</param>
/// <param name="UntilLocalDate"><c>yyyy-MM-dd</c>, or null.</param>
/// <param name="Count">Occurrence cap, or null.</param>
internal sealed record RecurrenceInput(
    string? Frequency,
    int? Interval,
    IReadOnlyList<string>? ByWeekday,
    string? UntilLocalDate,
    int? Count);

/// <summary>A due instant the store can persist, or a Spanish error for the model.</summary>
internal sealed record TimeResolution
{
    private TimeResolution()
    {
    }

    public DateTime LocalStart { get; private init; }

    public DateTimeOffset OccurrenceUtc { get; private init; }

    public string TimeZoneId { get; private init; } = "";

    public RecurrenceSpec? Recurrence { get; private init; }

    public string? DstNote { get; private init; }

    public string? Error { get; private init; }

    public static TimeResolution Ok(
        DateTime localStart,
        DateTimeOffset occurrenceUtc,
        string timeZoneId,
        RecurrenceSpec? recurrence,
        string? dstNote)
        => new()
        {
            LocalStart = localStart,
            OccurrenceUtc = occurrenceUtc,
            TimeZoneId = timeZoneId,
            Recurrence = recurrence,
            DstNote = dstNote,
        };

    public static TimeResolution Fail(string error) => new() { Error = error };
}

/// <summary>
/// Turns <c>due.in</c> or <c>due.local_datetime</c> into a UTC instant in the user's zone.
/// The model does not choose the offset.
/// </summary>
internal sealed class ReminderTimeResolver(
    TimeProvider clock,
    IUserTimeZoneProvider timeZones,
    IOptions<RemindersOptions> options)
{
    private static readonly string[] LocalFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    public async ValueTask<TimeResolution> ResolveAsync(
        Guid userId, DueInput due, RecurrenceInput? recurrence, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(due);
        var zone = await timeZones.GetTimeZoneAsync(userId, ct);
        var timeZoneId = ReminderTimeZones.CanonicalIana(zone);
        var now = clock.GetUtcNow();

        if (!TryResolveDue(due, zone, now, out var local, out var utc, out var dstNote, out var error))
        {
            return TimeResolution.Fail(error!);
        }

        var fromDuration = !string.IsNullOrWhiteSpace(due.Duration);
        if (!WithinHorizon(fromDuration, utc, now, options.Value, out error))
        {
            return TimeResolution.Fail(error!);
        }

        RecurrenceSpec? spec = null;
        if (recurrence is not null
            && !TryRecurrence(recurrence, local, out spec, out error))
        {
            return TimeResolution.Fail(error!);
        }

        return TimeResolution.Ok(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            ReminderFire.NormalizeUtc(utc),
            timeZoneId,
            spec,
            dstNote);
    }

    private static bool TryResolveDue(
        DueInput due,
        TimeZoneInfo zone,
        DateTimeOffset now,
        out DateTime local,
        out DateTimeOffset utc,
        out string? dstNote,
        out string? error)
    {
        local = default;
        utc = default;
        dstNote = null;
        var hasDuration = !string.IsNullOrWhiteSpace(due.Duration);
        var hasLocal = !string.IsNullOrWhiteSpace(due.LocalDateTime);
        if (hasDuration == hasLocal)
        {
            error = ReminderMessages.DueInvalidShape;
            return false;
        }

        if (hasDuration)
        {
            if (!TryDuration(due.Duration!, out var duration))
            {
                error = ReminderMessages.DueInvalidDuration;
                return false;
            }

            utc = now.Add(duration);
            local = Civil(utc, zone);
            error = null;
            return true;
        }

        if (!TryParseLocal(due.LocalDateTime!, out local))
        {
            error = ReminderMessages.DueInvalidLocal;
            return false;
        }

        utc = ToUtc(local, zone, out var adjustment);
        local = Civil(utc, zone);
        dstNote = adjustment switch
        {
            DstAdjustment.SkippedForward => ReminderMessages.DstSkipped,
            DstAdjustment.AmbiguousEarliest => ReminderMessages.DstAmbiguous,
            _ => null,
        };
        error = null;
        return true;
    }

    private static bool WithinHorizon(
        bool fromDuration,
        DateTimeOffset utc,
        DateTimeOffset now,
        RemindersOptions limits,
        out string? error)
    {
        var earliest = now.Add(limits.MinLeadTime);
        var latest = now.AddDays(limits.MaxHorizonDays);
        if (!fromDuration && utc < now)
        {
            error = ReminderMessages.DueInPast;
            return false;
        }

        if (utc < earliest)
        {
            error = ReminderMessages.DueTooSoon((int)limits.MinLeadTime.TotalSeconds);
            return false;
        }

        if (utc > latest)
        {
            error = ReminderMessages.DueTooFar;
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryRecurrence(
        RecurrenceInput input,
        DateTime localStart,
        out RecurrenceSpec? spec,
        out string? error)
    {
        spec = null;
        if (!TryFrequency(input.Frequency, out var frequency))
        {
            error = ReminderMessages.RecurrenceInvalid;
            return false;
        }

        var interval = input.Interval ?? 1;
        if (interval is < 1 or > 12)
        {
            error = ReminderMessages.RecurrenceInvalid;
            return false;
        }

        var untilSet = !string.IsNullOrWhiteSpace(input.UntilLocalDate);
        var countSet = input.Count is not null;
        if (untilSet && countSet)
        {
            error = ReminderMessages.RecurrenceInvalid;
            return false;
        }

        DateOnly? until = null;
        if (untilSet)
        {
            if (!DateOnly.TryParseExact(
                    input.UntilLocalDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedUntil))
            {
                error = ReminderMessages.RecurrenceInvalid;
                return false;
            }

            until = parsedUntil;
        }

        if (input.Count is < 1 or > 500)
        {
            error = ReminderMessages.RecurrenceInvalid;
            return false;
        }

        var weekdays = new List<DayOfWeek>();
        if (input.ByWeekday is not null)
        {
            if (frequency != RecurrenceFrequency.Weekly || input.ByWeekday.Count == 0)
            {
                error = ReminderMessages.RecurrenceInvalid;
                return false;
            }

            foreach (var code in input.ByWeekday)
            {
                if (!RecurrenceSpec.TryDay(code, out var day) || weekdays.Contains(day))
                {
                    error = ReminderMessages.RecurrenceInvalid;
                    return false;
                }

                weekdays.Add(day);
            }
        }

        spec = new RecurrenceSpec(frequency, interval, weekdays, until, input.Count);
        var next = RecurrenceCalculator.NextLocal(localStart, spec, localStart.AddTicks(-1), occurrencesFired: 0);
        if (next is null)
        {
            error = ReminderMessages.RecurrenceEmpty;
            spec = null;
            return false;
        }

        if (next.Value != localStart)
        {
            error = ReminderMessages.RecurrenceMismatch;
            spec = null;
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryFrequency(string? text, out RecurrenceFrequency frequency)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "daily":
                frequency = RecurrenceFrequency.Daily;
                return true;
            case "weekly":
                frequency = RecurrenceFrequency.Weekly;
                return true;
            case "monthly":
                frequency = RecurrenceFrequency.Monthly;
                return true;
            default:
                frequency = default;
                return false;
        }
    }

    private static bool TryDuration(string text, out TimeSpan duration)
    {
        duration = default;
        var trimmed = text.Trim().ToUpperInvariant();
        if (trimmed.Length == 0 || trimmed[0] != 'P')
        {
            return false;
        }

        try
        {
            duration = XmlConvert.ToTimeSpan(trimmed);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParseLocal(string text, out DateTime local)
    {
        local = default;
        var trimmed = text.Trim();
        var separator = trimmed.IndexOf('T');
        if (separator < 0)
        {
            return false;
        }

        var time = trimmed[(separator + 1)..];
        if (time.Contains('+') || time.Contains('-') || trimmed.EndsWith('Z'))
        {
            return false;
        }

        return DateTime.TryParseExact(
            trimmed,
            LocalFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out local);
    }

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo zone, out DstAdjustment adjustment)
    {
        var local = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            adjustment = DstAdjustment.SkippedForward;
            local = FirstValidLocal(zone, local);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        if (zone.IsAmbiguousTime(local))
        {
            adjustment = DstAdjustment.AmbiguousEarliest;
            return zone.GetAmbiguousTimeOffsets(local)
                .Select(offset => new DateTimeOffset(local, offset))
                .OrderBy(instant => instant.UtcDateTime)
                .First();
        }

        adjustment = DstAdjustment.None;
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTime FirstValidLocal(TimeZoneInfo zone, DateTime invalid)
    {
        var cursor = invalid;
        for (var minute = 0; minute < 180; minute++)
        {
            cursor = cursor.AddMinutes(1);
            if (!zone.IsInvalidTime(cursor))
            {
                return new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, cursor.Minute, 0);
            }
        }

        throw new InvalidOperationException("No valid local time exists within three hours of the gap.");
    }

    private static DateTime Civil(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var zoned = TimeZoneInfo.ConvertTime(utc, zone);
        return DateTime.SpecifyKind(zoned.DateTime, DateTimeKind.Unspecified);
    }

    private enum DstAdjustment
    {
        None = 0,
        SkippedForward = 1,
        AmbiguousEarliest = 2,
    }
}

/// <summary>Canonical IANA id stored on the row. <c>America/Buenos_Aires</c> folds to Argentina.</summary>
internal static class ReminderTimeZones
{
    public static string CanonicalIana(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.Id.Contains('/', StringComparison.Ordinal))
        {
            return FoldAlias(zone.Id);
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana))
        {
            return FoldAlias(iana);
        }

        return FoldAlias(zone.Id);
    }

    private static string FoldAlias(string id)
        => string.Equals(id, "America/Buenos_Aires", StringComparison.Ordinal)
            ? "America/Argentina/Buenos_Aires"
            : id;
}
