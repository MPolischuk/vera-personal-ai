namespace Assistant.Channels.Inbox;

/// <summary>Local 04:10 in America/Argentina/Buenos_Aires. Key <c>purge:inbox:{yyyy-MM-dd}</c>.</summary>
internal static class InboxPurgeSchedule
{
    public static readonly TimeOnly LocalTime = new(4, 10);

    public const string TimeZoneId = "America/Argentina/Buenos_Aires";

    public static DateTimeOffset TodaySlot(DateTimeOffset utcNow)
    {
        var zone = Zone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        return ToUtc(localDate.ToDateTime(LocalTime), zone);
    }

    public static DateTimeOffset NextRun(DateTimeOffset utcNow)
    {
        var slot = TodaySlot(utcNow);
        if (slot > utcNow)
        {
            return slot;
        }

        var zone = Zone();
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var tomorrow = DateOnly.FromDateTime(local.DateTime).AddDays(1);
        return ToUtc(tomorrow.ToDateTime(LocalTime), zone);
    }

    public static string KeyFor(DateTimeOffset slotUtc)
    {
        var local = TimeZoneInfo.ConvertTime(slotUtc, Zone());
        return $"purge:inbox:{DateOnly.FromDateTime(local.DateTime):yyyy-MM-dd}";
    }

    private static TimeZoneInfo Zone()
        => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo zone)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified),
            zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
