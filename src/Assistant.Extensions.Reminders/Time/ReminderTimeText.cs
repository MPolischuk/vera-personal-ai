namespace Assistant.Extensions.Reminders.Time;

/// <summary>Civil phrases the model repeats back to the user.</summary>
internal static class ReminderTimeText
{
    public static string LocalDue(DateTime local)
        => $"{RecurrenceSpec.SpanishDay(local.DayOfWeek)} {local.Day}/{local.Month} {local:HH:mm}";

    public static string Relative(DateTime local, DateTime nowLocal)
    {
        var span = local - nowLocal;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var minutes = (int)Math.Round(span.TotalMinutes);
        if (minutes < 1)
        {
            return "en menos de un minuto";
        }

        if (minutes < 60)
        {
            return minutes == 1 ? "en 1 minuto" : FormattableString.Invariant($"en {minutes} minutos");
        }

        var hours = (int)Math.Round(span.TotalHours);
        if (hours < 36)
        {
            return hours == 1 ? "en 1 hora" : FormattableString.Invariant($"en {hours} horas");
        }

        var days = (int)Math.Round(span.TotalDays);
        return days == 1 ? "en 1 día" : FormattableString.Invariant($"en {days} días");
    }

    public static string ChannelHint(string channel) => channel switch
    {
        "telegram" => "te aviso por Telegram",
        "webpush" => "te aviso por la web",
        _ => "te aviso por acá",
    };

    public static DateTime NowLocal(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var zoned = TimeZoneInfo.ConvertTime(utc, zone);
        return DateTime.SpecifyKind(zoned.DateTime, DateTimeKind.Unspecified);
    }
}
