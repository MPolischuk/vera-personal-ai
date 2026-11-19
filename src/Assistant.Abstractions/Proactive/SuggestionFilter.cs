namespace Assistant.Abstractions.Proactive;

/// <summary>Wire values for <c>suggestion_audits.outcome</c>.</summary>
public static class SuggestionOutcomes
{
    public const string Delivered = "delivered";

    public const string Discarded = "discarded";

    /// <summary>Stored when a delivery has no reason of its own.</summary>
    public const string DeliveredReason = "delivered";
}

/// <summary>Why <see cref="SuggestionFilter"/> discarded a candidate. Closed list.</summary>
public static class SuggestionDiscardReasons
{
    public const string QuietHours = "quiet_hours";

    public const string DailyCap = "daily_cap";

    public const string NoSource = "no_source";

    public const string RepeatedNotHelpful = "repeated_not_helpful";

    public const string Empty = "empty";
}

/// <summary>Quiet window and daily cap. The reminder quiet hours are a different option.</summary>
public sealed record SuggestionWindow(TimeOnly QuietStart, TimeOnly QuietEnd, int MaxPerDay)
{
    public static SuggestionWindow Default { get; } = new(new TimeOnly(22, 0), new TimeOnly(8, 0), 3);
}

/// <summary>One candidate the filter judges. It does not call a model.</summary>
public sealed record SuggestionCandidate(
    string? Text,
    string? SourceRef,
    TimeOnly LocalTime,
    int DeliveredToday,
    bool MarkedNotHelpfulToday);

/// <summary><see cref="Deliver"/> is false when <see cref="Reason"/> is a discard code.</summary>
public sealed record SuggestionVerdict(bool Deliver, string Reason);

/// <summary>
/// Code between a heartbeat reply and the publisher.
/// Order: empty, quiet hours, missing source, not-helpful today, then the daily cap.
/// </summary>
public static class SuggestionFilter
{
    public static SuggestionVerdict Evaluate(SuggestionCandidate candidate, SuggestionWindow window)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(window);

        if (string.IsNullOrWhiteSpace(candidate.Text))
        {
            return Discard(SuggestionDiscardReasons.Empty);
        }

        if (IsQuiet(candidate.LocalTime, window.QuietStart, window.QuietEnd))
        {
            return Discard(SuggestionDiscardReasons.QuietHours);
        }

        if (string.IsNullOrWhiteSpace(candidate.SourceRef))
        {
            return Discard(SuggestionDiscardReasons.NoSource);
        }

        if (candidate.MarkedNotHelpfulToday)
        {
            return Discard(SuggestionDiscardReasons.RepeatedNotHelpful);
        }

        if (candidate.DeliveredToday >= window.MaxPerDay)
        {
            return Discard(SuggestionDiscardReasons.DailyCap);
        }

        return new SuggestionVerdict(true, SuggestionOutcomes.DeliveredReason);
    }

    /// <summary>
    /// True from <paramref name="quietStart"/> inclusive until <paramref name="quietEnd"/> exclusive.
    /// A start later than the end wraps past midnight. Equal bounds are not a quiet window.
    /// </summary>
    public static bool IsQuiet(TimeOnly localTime, TimeOnly quietStart, TimeOnly quietEnd)
    {
        if (quietStart == quietEnd)
        {
            return false;
        }

        if (quietStart < quietEnd)
        {
            return localTime >= quietStart && localTime < quietEnd;
        }

        return localTime >= quietStart || localTime < quietEnd;
    }

    private static SuggestionVerdict Discard(string reason) => new(false, reason);
}
