namespace Assistant.Abstractions.Proactive;

/// <summary>One row of <c>suggestion_audits</c>.</summary>
public sealed record SuggestionAudit(
    Guid Id,
    Guid UserId,
    DateOnly LocalDate,
    string Outcome,
    string Reason,
    string? SourceRef,
    DateTimeOffset CreatedAt);

/// <summary>Insert of one audit. Outcome is <see cref="SuggestionOutcomes"/>.</summary>
public sealed record NewSuggestionAudit(
    Guid UserId,
    DateOnly LocalDate,
    string Outcome,
    string Reason,
    string? SourceRef);

/// <summary>Delivered and discarded suggestions. A silent pulse still writes a discarded row.</summary>
public interface ISuggestionAuditStore
{
    public ValueTask<SuggestionAudit> InsertAsync(NewSuggestionAudit draft, DateTimeOffset now, CancellationToken ct);

    public ValueTask<int> CountDeliveredAsync(Guid userId, DateOnly localDate, CancellationToken ct);
}
