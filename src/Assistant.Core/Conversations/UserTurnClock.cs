using System.Globalization;

namespace Assistant.Core.Conversations;

/// <summary>
/// Local date, time, and elapsed time for the user <see cref="Microsoft.Extensions.AI.ChatMessage"/>
/// sent to the model. The persisted row stays the bare user text.
/// </summary>
internal static class UserTurnClock
{
    public static string Decorate(
        string userText,
        DateTimeOffset utcNow,
        string timeZoneId,
        string cultureName,
        DateTimeOffset? previousUserUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var stamp = local.ToString("f", culture);
        var line = previousUserUtc is { } previous
            ? $"[{stamp} · {Elapsed(utcNow - previous)} después del mensaje anterior]"
            : $"[{stamp}]";

        return line + "\n" + userText;
    }

    private static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "menos de un minuto";
        }

        if (elapsed.TotalHours < 1)
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        }

        if (elapsed.TotalDays < 1)
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hora" : $"{hours} horas";
        }

        var days = (int)elapsed.TotalDays;
        return days == 1 ? "1 día" : $"{days} días";
    }
}
