namespace Assistant.Abstractions.Proactive;

/// <summary>Job kind, keys and the 30-minute grid in <see cref="BriefingJobs.TimeZoneId"/>.</summary>
public static class HeartbeatJobs
{
    public const string Kind = "proactive.heartbeat";

    public const string TaskKind = "heartbeat";

    public const string NotificationTitle = "Sugerencia";

    public const string DefaultChecklist = "calendario de las próximas dos horas";

    public const int EveryMinutes = 30;

    public const int PostponeMinutes = 5;

    public const int ConversationScan = 20;

    public const int MaxToolRounds = 4;

    public static string JobKey(Guid userId)
        => FormattableString.Invariant($"proactive.heartbeat:{userId:D}");

    /// <summary>Notification and task key <c>heartbeat:{userId}:{yyyy-MM-dd}:{n}</c>.</summary>
    public static string NotificationKey(Guid userId, DateOnly localDate, int deliveryNumber)
        => FormattableString.Invariant($"heartbeat:{userId:D}:{localDate:yyyy-MM-dd}:{deliveryNumber}");
}

/// <summary>Payload of <c>proactive.heartbeat</c>.</summary>
public sealed record HeartbeatJobPayload(Guid UserId);

/// <summary>Next run of the heartbeat. Outside 08:00–22:00 the next run is the next open.</summary>
public static class HeartbeatSchedule
{
    public static TimeOnly LocalTime(DateTimeOffset utc)
        => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, BriefingClock.Zone).DateTime);

    public static DateOnly LocalDate(DateTimeOffset utc) => BriefingClock.LocalDate(utc);

    /// <summary>
    /// The next strictly future slot. A slot that lands in quiet hours becomes the next open
    /// (<paramref name="quietEnd"/>, which is 08:00 by default).
    /// </summary>
    public static DateTimeOffset Next(
        DateTimeOffset utc,
        TimeOnly quietStart,
        TimeOnly quietEnd,
        int everyMinutes)
    {
        if (everyMinutes < 1)
        {
            everyMinutes = HeartbeatJobs.EveryMinutes;
        }

        if (SuggestionFilter.IsQuiet(LocalTime(utc), quietStart, quietEnd))
        {
            return NextOpen(utc, quietEnd);
        }

        var zoned = TimeZoneInfo.ConvertTime(utc, BriefingClock.Zone);
        var slot = NextSlot(zoned.DateTime, everyMinutes);
        if (SuggestionFilter.IsQuiet(TimeOnly.FromDateTime(slot), quietStart, quietEnd))
        {
            return NextOpen(utc, quietEnd);
        }

        return At(slot);
    }

    public static DateTimeOffset NextOpen(DateTimeOffset utc, TimeOnly quietEnd)
    {
        var zoned = TimeZoneInfo.ConvertTime(utc, BriefingClock.Zone);
        var date = DateOnly.FromDateTime(zoned.DateTime);
        var today = At(date.ToDateTime(quietEnd));
        return utc < today ? today : At(date.AddDays(1).ToDateTime(quietEnd));
    }

    private static DateTime NextSlot(DateTime local, int everyMinutes)
    {
        var minutes = (local.Hour * 60) + local.Minute;
        var truncated = new DateTime(
            local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
        var partial = local != truncated;
        var nextMinutes = minutes % everyMinutes == 0 && !partial
            ? minutes + everyMinutes
            : ((minutes / everyMinutes) + 1) * everyMinutes;
        var day = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return day.AddMinutes(nextMinutes);
    }

    private static DateTimeOffset At(DateTime unspecified)
    {
        var local = DateTime.SpecifyKind(unspecified, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, BriefingClock.Zone.GetUtcOffset(local));
    }
}
