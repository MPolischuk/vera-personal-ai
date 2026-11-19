using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Time;

namespace Assistant.Extensions.Proactive;

/// <summary>
/// Resolves a saved phrase to today's briefing. It does not call a model and does not write memory.
/// </summary>
public sealed class BriefingShortcut(
    IPhraseShortcutStore shortcuts,
    IBriefingStore briefings,
    IUserTimeZoneProvider timeZones,
    TimeProvider clock)
{
    public async ValueTask<string?> TryResolveAsync(Guid userId, string? userText, CancellationToken ct)
    {
        if (!PhraseNormalizer.TryNormalize(userText, out var phrase))
        {
            return null;
        }

        var action = await shortcuts.FindActionAsync(userId, phrase, ct);
        if (!string.Equals(action, PhraseActions.BriefingLatest, StringComparison.Ordinal))
        {
            return null;
        }

        TimeZoneInfo zone;
        try
        {
            zone = await timeZones.GetTimeZoneAsync(userId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            zone = BriefingClock.Zone;
        }

        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var briefing = await briefings.FindByLocalDateAsync(userId, local, ct);
        return BriefingText.Render(briefing);
    }
}
