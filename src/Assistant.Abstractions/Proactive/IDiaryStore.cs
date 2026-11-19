namespace Assistant.Abstractions.Proactive;

/// <summary>Wire values for <c>consolidation_diary.outcome</c>.</summary>
public static class DiaryOutcomes
{
    public const string Proposed = "proposed";

    public const string Approved = "approved";

    public const string RejectedVersion = "rejected_version";

    public const string RejectedScan = "rejected_scan";

    public const string Undone = "undone";

    /// <summary>Shown in the diary. It does not include the memory text.</summary>
    public const string ScanReason = "Carácter de formato.";

    public static bool IsKnown(string? value)
        => value is Proposed or Approved or RejectedVersion or RejectedScan or Undone;
}

public sealed record DiaryEntry(
    Guid Id,
    Guid UserId,
    Guid? ProposalId,
    Guid? PreviousId,
    Guid? SuccessorId,
    string Reason,
    string Outcome,
    DateTimeOffset CreatedAt,
    string? ProposalStatus);

public sealed record NewDiaryEntry(
    Guid UserId,
    Guid? ProposalId,
    Guid? PreviousId,
    Guid? SuccessorId,
    string Reason,
    string Outcome);

/// <summary>Contradiction diary and the proposal status the diary applies.</summary>
public interface IDiaryStore
{
    public ValueTask<DiaryEntry> AppendAsync(NewDiaryEntry draft, CancellationToken ct);

    public ValueTask<IReadOnlyList<DiaryEntry>> ListAsync(Guid userId, CancellationToken ct);

    public ValueTask<DiaryEntry?> FindAsync(Guid entryId, Guid userId, CancellationToken ct);

    public ValueTask<Proposal?> FindProposalAsync(Guid proposalId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Moves a pending proposal to <paramref name="status"/>. False when it is missing or no longer pending.
    /// </summary>
    public ValueTask<bool> TrySetProposalStatusAsync(
        Guid proposalId,
        Guid userId,
        string status,
        DateTimeOffset resolvedAt,
        CancellationToken ct);
}

/// <summary>Job <c>memory.review</c>, Sunday 04:00 in <see cref="BriefingJobs.TimeZoneId"/>.</summary>
public static class MemoryReviewJobs
{
    public const string Kind = "memory.review";

    public const string TaskKind = "consolidation";

    public const string TaskTitle = "Revisión de memoria";

    public const int LocalHour = 4;

    public const int MaxInputCharacters = 8000;

    public const int MaxOutputTokens = 800;

    public static string JobKey(DateOnly localDate)
        => FormattableString.Invariant($"memory.review:{localDate:yyyy-MM-dd}");

    public static string TaskKey(Guid userId, DateOnly localDate)
        => FormattableString.Invariant($"memory.review:{userId:D}:{localDate:yyyy-MM-dd}");

    public static DateTimeOffset AtFour(DateOnly date)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, LocalHour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, BriefingClock.Zone.GetUtcOffset(local));
    }

    public static DateOnly LocalDate(DateTimeOffset utc) => BriefingClock.LocalDate(utc);

    /// <summary>The next Sunday 04:00 that is still strictly in the future.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset utc)
    {
        var date = LocalDate(utc);
        var days = ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7;
        var sunday = date.AddDays(days);
        var at = AtFour(sunday);
        return utc < at ? at : AtFour(sunday.AddDays(7));
    }
}

/// <summary>Payload of <c>memory.review</c>. The date is the local Sunday.</summary>
public sealed record MemoryReviewPayload(DateOnly LocalDate);
