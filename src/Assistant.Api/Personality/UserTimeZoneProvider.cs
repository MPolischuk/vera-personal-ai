using Assistant.Abstractions.Time;
using Assistant.Core.Personality;

namespace Assistant.Api.Personality;

/// <summary>
/// The personality profile is the user's time zone. <c>America/Buenos_Aires</c> is normalized
/// to <c>America/Argentina/Buenos_Aires</c>.
/// </summary>
internal sealed class UserTimeZoneProvider(IPersonalityProfileProvider profiles) : IUserTimeZoneProvider
{
    public async ValueTask<TimeZoneInfo> GetTimeZoneAsync(Guid userId, CancellationToken ct)
    {
        _ = userId;
        var profile = await profiles.LoadAsync(ct);
        return TimeZoneIds.Resolve(profile.TimeZone);
    }
}

internal static class TimeZoneIds
{
    public static TimeZoneInfo Resolve(string id)
    {
        var normalized = string.Equals(id, "America/Buenos_Aires", StringComparison.Ordinal)
            ? "America/Argentina/Buenos_Aires"
            : id;

        if (TimeZoneInfo.TryFindSystemTimeZoneById(normalized, out var zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(normalized, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(normalized, out var ianaId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out zone))
        {
            return zone;
        }

        throw new TimeZoneNotFoundException($"Time zone '{id}' is not a known identifier.");
    }
}
