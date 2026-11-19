using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Delivery;

/// <summary>Quiet-hours decision for one notification.</summary>
public abstract record QuietHoursDecision
{
    public sealed record Deliver : QuietHoursDecision;

    public sealed record DeliverSilently : QuietHoursDecision;

    public sealed record Defer(DateTimeOffset Until) : QuietHoursDecision;

    public sealed record Drop : QuietHoursDecision;
}

/// <summary>
/// Applies the quiet-hours table in the user's time zone.
/// A reminder more than <see cref="LateAfter"/> past <c>DueAt</c> is late.
/// </summary>
public sealed class QuietHoursPolicy(
    IUserTimeZoneProvider timeZones,
    TimeProvider clock,
    IOptions<DeliveryOptions> options)
{
    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);

    public async ValueTask<QuietHoursDecision> EvaluateAsync(
        NotificationKind kind,
        DateTimeOffset? dueAt,
        bool allowDuringQuietHours,
        Guid userId,
        CancellationToken ct)
    {
        var current = options.Value;
        if (allowDuringQuietHours || !current.QuietHours.Enabled)
        {
            return new QuietHoursDecision.Deliver();
        }

        var zone = await timeZones.GetTimeZoneAsync(userId, ct);
        var now = clock.GetUtcNow();
        if (!TryParseTime(current.QuietHours.Start, out var start)
            || !TryParseTime(current.QuietHours.End, out var end))
        {
            throw new InvalidOperationException("Delivery:QuietHours must be HH:mm.");
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var localTime = TimeOnly.FromDateTime(local.DateTime);
        if (!IsQuiet(localTime, start, end))
        {
            return new QuietHoursDecision.Deliver();
        }

        var late = dueAt is { } due && now - due > LateAfter;
        return Decide(kind, late, now, zone, end);
    }

    public static bool TryParseTime(string? text, out TimeOnly time)
        => TimeOnly.TryParseExact(
            text,
            ["HH:mm", "H:mm"],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out time);

    internal static bool IsQuiet(TimeOnly local, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return false;
        }

        if (start < end)
        {
            return local >= start && local < end;
        }

        return local >= start || local < end;
    }

    private static QuietHoursDecision Decide(
        NotificationKind kind,
        bool late,
        DateTimeOffset now,
        TimeZoneInfo zone,
        TimeOnly end)
    {
        QuietHoursDecision Defer() => new QuietHoursDecision.Defer(EndOfQuiet(now, zone, end));

        return kind switch
        {
            NotificationKind.Reminder => late ? Defer() : new QuietHoursDecision.Deliver(),
            NotificationKind.ConfirmationRequest => new QuietHoursDecision.Deliver(),
            NotificationKind.Proactive => new QuietHoursDecision.Drop(),
            NotificationKind.TaskFinished => Defer(),
            NotificationKind.ExternalDigest => Defer(),
            NotificationKind.System => Defer(),
            _ => Defer(),
        };
    }

    internal static DateTimeOffset EndOfQuiet(DateTimeOffset utcNow, TimeZoneInfo zone, TimeOnly end)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var endLocal = DateOnly.FromDateTime(local.DateTime).ToDateTime(end, DateTimeKind.Unspecified);
        if (endLocal <= local.DateTime)
        {
            endLocal = endLocal.AddDays(1);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
