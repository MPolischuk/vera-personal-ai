using System.Globalization;

namespace Assistant.Abstractions.Proactive;

/// <summary>Wire values for <c>goals.status</c>.</summary>
public static class GoalStatuses
{
    public const string Active = "active";

    public const string Done = "done";
}

/// <summary>Wire values for <c>automations.cadence</c>.</summary>
public static class AutomationCadence
{
    public const string Once = "once";

    public const string Interval = "interval";

    public const string Daily = "daily";

    public const string SystemDaily = "system_daily";
}

/// <summary>Wire values for <c>automations.status</c>.</summary>
public static class AutomationStatuses
{
    public const string Active = "active";

    public const string Paused = "paused";
}

/// <summary>Wire values for <c>briefing_items.block</c>.</summary>
public static class BriefingBlocks
{
    public const string Today = "today";

    public const string Ahead = "ahead";
}

/// <summary>Wire values for <c>briefing_items.triage</c>.</summary>
public static class BriefingTriage
{
    public const string NeedsYou = "needs_you";

    public const string CanWait = "can_wait";
}

/// <summary>Wire values for <c>briefing_items.feedback</c>.</summary>
public static class BriefingFeedback
{
    public const string Done = "done";

    public const string Dismissed = "dismissed";

    public const string Helpful = "helpful";

    public const string NotHelpful = "not_helpful";

    public static bool IsKnown(string? value)
        => value is Done or Dismissed or Helpful or NotHelpful;
}

/// <summary>The only phrase action this step stores.</summary>
public static class PhraseActions
{
    public const string BriefingLatest = "briefing.latest";
}

/// <summary>Job kind and idempotency keys for the 08:00 briefing.</summary>
public static class BriefingJobs
{
    public const string GenerateKind = "briefing.generate";

    public const string TimeZoneId = "America/Argentina/Buenos_Aires";

    public const int LocalHour = 8;

    public const string TaskKind = "briefing";

    public const string NotificationTitle = "Briefing";

    /// <summary>Notification dedup key <c>briefing:{userId}:{yyyy-MM-dd}</c>.</summary>
    public static string NotificationKey(Guid userId, DateOnly localDate)
        => FormattableString.Invariant($"briefing:{userId:D}:{localDate:yyyy-MM-dd}");

    /// <summary>Scheduler key for that local date. A finished row must not block the next day.</summary>
    public static string JobKey(Guid userId, DateOnly localDate)
        => FormattableString.Invariant($"briefing.generate:{userId:D}:{localDate:yyyy-MM-dd}");

    /// <summary>Task idempotency key. One row per user and local date.</summary>
    public static string TaskKey(Guid userId, DateOnly localDate)
        => NotificationKey(userId, localDate);
}

/// <summary>Local 08:00 in <see cref="BriefingJobs.TimeZoneId"/>.</summary>
public static class BriefingClock
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(BriefingJobs.TimeZoneId);

    public static DateOnly LocalDate(DateTimeOffset utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);

    /// <summary>The next 08:00 that is still strictly in the future, or today at 08:00 when it has not passed.</summary>
    public static DateTimeOffset NextLocalEight(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        var todayAtEight = AtLocalEight(day);
        return utc < todayAtEight ? todayAtEight : AtLocalEight(day.AddDays(1));
    }

    public static DateTimeOffset AtLocalEight(DateOnly date)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, BriefingJobs.LocalHour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }
}

/// <summary>Payload of <c>briefing.generate</c>.</summary>
public sealed record BriefingGeneratePayload(Guid UserId, DateOnly LocalDate);

/// <summary>
/// Trim, invariant lower case, collapsed whitespace, at most 40 characters.
/// </summary>
public static class PhraseNormalizer
{
    public const int MaxLength = 40;

    public static bool TryNormalize(string? phrase, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var collapsed = Collapse(phrase.Trim().ToLowerInvariant());
        if (collapsed.Length is < 1 or > MaxLength)
        {
            return false;
        }

        normalized = collapsed;
        return true;
    }

    private static string Collapse(string text)
    {
        var buffer = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (buffer.Length > 0)
                {
                    pendingSpace = true;
                }

                continue;
            }

            if (pendingSpace)
            {
                buffer.Append(' ');
                pendingSpace = false;
            }

            buffer.Append(character);
        }

        return buffer.ToString();
    }
}

/// <summary>Shared checks for a user automation. The system briefing does not go through here.</summary>
public static class AutomationRules
{
    public const int MinIntervalMinutes = 30;

    public const int TitleMaxLength = 80;

    public const int InstructionMaxLength = 2000;

    public static bool IsUserCadence(string? cadence)
        => cadence is AutomationCadence.Once or AutomationCadence.Interval or AutomationCadence.Daily;

    public static bool TryParseLocalTime(string? text, out string localTime)
    {
        localTime = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!TimeOnly.TryParseExact(
                text.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        localTime = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>One explicit goal. Chat text never becomes a row.</summary>
public sealed record Goal(
    Guid Id,
    Guid UserId,
    string Text,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A phrase the turn runner resolves before the orchestrator.</summary>
public sealed record PhraseShortcut(Guid Id, Guid UserId, string PhraseNormalized, string Action);

/// <summary>A stored automation, including the instruction the list tool does not return.</summary>
public sealed record Automation(
    Guid Id,
    Guid UserId,
    string Title,
    string Instruction,
    string Cadence,
    string? LocalTime,
    int? IntervalMinutes,
    string Status,
    Guid ConversationId,
    DateTimeOffset? NextRunAt);

/// <summary>What <c>list_automations</c> may return. No instruction.</summary>
public sealed record AutomationSummary(
    Guid Id,
    string Title,
    string Cadence,
    string Status,
    DateTimeOffset? NextRunAt);

/// <summary>Input for a user automation. <see cref="AutomationCadence.SystemDaily"/> is rejected.</summary>
public sealed record NewUserAutomation(
    Guid UserId,
    string Title,
    string Instruction,
    string Cadence,
    string? LocalTime,
    int? IntervalMinutes,
    Guid ConversationId,
    DateTimeOffset? RunAt);

/// <summary>One item inside a briefing.</summary>
public sealed record BriefingItem(
    Guid Id,
    Guid BriefingId,
    string Block,
    string Title,
    string Reason,
    string Triage,
    string Source,
    string? SourceRef,
    string? Feedback,
    DateTimeOffset? FeedbackAt);

/// <summary>The one briefing for a user and a local date.</summary>
public sealed record Briefing(
    Guid Id,
    Guid UserId,
    DateOnly LocalDate,
    string Summary,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BriefingItem> Items);

/// <summary>Item to insert with a new briefing. Feedback starts empty.</summary>
public sealed record NewBriefingItem(
    string Block,
    string Title,
    string Reason,
    string Triage,
    string Source,
    string? SourceRef);

/// <summary>Insert of one briefing. A repeated local date returns the existing row.</summary>
public sealed record NewBriefing(
    Guid UserId,
    DateOnly LocalDate,
    string Summary,
    IReadOnlyList<NewBriefingItem> Items);

/// <summary>Result of saving a briefing. <see cref="Created"/> is false when the local date already had a row.</summary>
public sealed record BriefingSave(Briefing Briefing, bool Created);

/// <summary>Explicit goals. Nothing here is inferred from chat.</summary>
public interface IGoalStore
{
    public ValueTask<Goal> InsertAsync(Guid userId, string text, DateTimeOffset now, CancellationToken ct);

    public ValueTask<bool> CompleteAsync(Guid userId, Guid goalId, DateTimeOffset now, CancellationToken ct);

    public ValueTask<IReadOnlyList<Goal>> ListActiveAsync(Guid userId, CancellationToken ct);
}

/// <summary>Phrase shortcuts. The stored phrase is already normalized.</summary>
public interface IPhraseShortcutStore
{
    public ValueTask<PhraseShortcut> SetAsync(
        Guid userId, string phraseNormalized, string action, CancellationToken ct);

    public ValueTask<string?> FindActionAsync(Guid userId, string phraseNormalized, CancellationToken ct);
}

/// <summary>User automations and the system briefing row.</summary>
public interface IAutomationStore
{
    public ValueTask<Automation> InsertUserAsync(NewUserAutomation draft, DateTimeOffset now, CancellationToken ct);

    public ValueTask<Automation> EnsureSystemBriefingAsync(
        Guid userId, Guid conversationId, DateTimeOffset nextRunAt, CancellationToken ct);

    public ValueTask<Automation?> FindSystemBriefingAsync(Guid userId, CancellationToken ct);

    public ValueTask<IReadOnlyList<AutomationSummary>> ListAsync(Guid userId, CancellationToken ct);

    public ValueTask<Automation?> FindAsync(Guid userId, Guid automationId, CancellationToken ct);

    /// <summary>False when the row is missing, belongs to someone else, or is the system briefing.</summary>
    public ValueTask<bool> PauseAsync(Guid userId, Guid automationId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Deletes a user automation. False for the system briefing.
    /// The caller cancels the scheduled job.
    /// </summary>
    public ValueTask<bool> DeleteAsync(Guid userId, Guid automationId, CancellationToken ct);

    /// <summary>Updates the next run and, when set, the status. False for a missing row or the system briefing.</summary>
    public ValueTask<bool> SetNextRunAsync(
        Guid userId,
        Guid automationId,
        DateTimeOffset? nextRunAt,
        string? status,
        CancellationToken ct);
}

/// <summary>Payload of <c>automation.run</c>.</summary>
public sealed record AutomationRunPayload(Guid UserId, Guid AutomationId);

/// <summary>Keys for a user automation. The system briefing uses <see cref="BriefingJobs"/> instead.</summary>
public static class AutomationJobs
{
    public const string RunKind = "automation.run";

    public const string TaskKind = "automation";

    public static string Key(Guid automationId, DateTimeOffset runAt)
        => FormattableString.Invariant($"automation:{automationId:D}:{runAt.UtcDateTime:yyyyMMddHHmmss}");

    public static string TaskKey(Guid automationId, DateTimeOffset runAt)
        => FormattableString.Invariant($"automation.task:{automationId:D}:{runAt.UtcDateTime:yyyyMMddHHmmss}");
}

/// <summary>One briefing per user and local date, plus feedback on its items.</summary>
public interface IBriefingStore
{
    public ValueTask<Briefing?> FindByLocalDateAsync(Guid userId, DateOnly localDate, CancellationToken ct);

    public ValueTask<Briefing?> FindAsync(Guid userId, Guid briefingId, CancellationToken ct);

    public ValueTask<BriefingSave> SaveAsync(NewBriefing draft, CancellationToken ct);

    public ValueTask<bool> SetFeedbackAsync(
        Guid userId,
        Guid briefingId,
        Guid itemId,
        string feedback,
        DateTimeOffset now,
        CancellationToken ct);
}

/// <summary>Refresh token ciphertext for one user and one MCP server. No access token.</summary>
public sealed record McpOAuthCredential(Guid UserId, string ServerId, string RefreshProtected, DateTimeOffset UpdatedAt);

/// <summary>OAuth refresh tokens protected by <see cref="Assistant.Abstractions.Security.ISecretBox"/>.</summary>
public interface IMcpOAuthTokenStore
{
    public ValueTask SaveRefreshAsync(
        Guid userId, string serverId, string refreshProtected, DateTimeOffset now, CancellationToken ct);

    public ValueTask<bool> HasRefreshAsync(Guid userId, string serverId, CancellationToken ct);

    /// <summary>
    /// The single credential for <paramref name="serverId"/>, or null when there is not exactly one.
    /// The connect path has no user id; two rows leave the server disconnected.
    /// </summary>
    public ValueTask<McpOAuthCredential?> FindSingleAsync(string serverId, CancellationToken ct);
}

/// <summary>
/// Turns a stored refresh token into an access token in memory.
/// A failure leaves the caller with null and the server disconnected.
/// </summary>
public interface IMcpAccessTokenRefresher
{
    public ValueTask<string?> GetAccessTokenAsync(string serverId, CancellationToken ct);
}
