using Assistant.Abstractions.Proactive;
using Assistant.Persistence.Entities;

namespace Assistant.Persistence.Proactive;

internal sealed class GoalEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Status { get; set; } = GoalStatusesWire.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal static class GoalStatusesWire
{
    public const string Active = "active";

    public const string Done = "done";
}

internal sealed class PhraseShortcutEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PhraseNormalized { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}

internal sealed class AutomationEntity : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Instruction { get; set; } = string.Empty;

    public string Cadence { get; set; } = string.Empty;

    public string? LocalTime { get; set; }

    public int? IntervalMinutes { get; set; }

    public string Status { get; set; } = "active";

    public Guid ConversationId { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class BriefingEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateOnly LocalDate { get; set; }

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public List<BriefingItemEntity> Items { get; set; } = [];
}

internal sealed class BriefingItemEntity
{
    public Guid Id { get; set; }

    public Guid BriefingId { get; set; }

    public string Block { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Triage { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? SourceRef { get; set; }

    public string? Feedback { get; set; }

    public DateTimeOffset? FeedbackAt { get; set; }

    public BriefingEntity Briefing { get; set; } = null!;
}

internal sealed class SuggestionAuditEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateOnly LocalDate { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? SourceRef { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ProposalEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Origin { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string ArgumentsJson { get; set; } = "{}";

    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public DateTimeOffset? SubjectUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}

internal sealed class HeartbeatCheckEntity
{
    public Guid UserId { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class StandingOrderEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class DiaryEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ProposalId { get; set; }

    public Guid? PreviousId { get; set; }

    public Guid? SuccessorId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Outcome { get; set; } = DiaryOutcomes.Proposed;

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ConditionalIntentEntity : IHasCreatedAt
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string EventName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
}
