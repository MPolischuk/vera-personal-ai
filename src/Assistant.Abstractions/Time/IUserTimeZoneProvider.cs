namespace Assistant.Abstractions.Time;

/// <summary>
/// Provides the user's time zone.
/// </summary>
/// <remarks>
/// The time zone comes from the personality profile (<c>personality.timezone</c>).
/// It is invariant for most operations; it is used to interpret local times
/// (reminders, quiet hours) and to display dates.
/// </remarks>
public interface IUserTimeZoneProvider
{
    /// <summary>
    /// Gets the user's time zone.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user's <see cref="TimeZoneInfo"/>.</returns>
    public ValueTask<TimeZoneInfo> GetTimeZoneAsync(Guid userId, CancellationToken ct);
}
