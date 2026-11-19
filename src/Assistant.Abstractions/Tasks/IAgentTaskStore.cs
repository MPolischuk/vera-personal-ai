namespace Assistant.Abstractions.Tasks;

/// <summary>Insert of one task. A repeated idempotency key returns the existing row.</summary>
public sealed record AgentTaskInsert(
    Guid UserId,
    Guid ConversationId,
    Guid OriginTurnId,
    string IdempotencyKey,
    string Kind,
    string Title,
    string Instruction,
    decimal CostCapUsd,
    DateTimeOffset DeadlineAt,
    bool Idempotent,
    Guid? UsageTurnId = null,
    string State = AgentTaskStatuses.Submitted,
    bool ArmNotice = true,
    decimal CostUsd = 0,
    string? FailureReason = null);

/// <summary>A stored task, including fields the view and the notice do not show.</summary>
public sealed record AgentTaskRow(
    Guid Id,
    Guid UserId,
    Guid ConversationId,
    Guid OriginTurnId,
    Guid? UsageTurnId,
    string IdempotencyKey,
    string Kind,
    string Title,
    string Instruction,
    string State,
    string? FailureReason,
    string? PartialResult,
    decimal CostUsd,
    decimal CostCapUsd,
    DateTimeOffset DeadlineAt,
    bool Idempotent,
    int RestartCount,
    string? ExternalTaskId,
    bool NoticeArmed,
    string? LockedBy,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>A task claimed by one worker. The shell passes this to <see cref="IAgentWorker"/>.</summary>
public sealed record ClaimedAgentTask(
    Guid Id,
    Guid UserId,
    Guid ConversationId,
    string Kind,
    string Title,
    string Instruction,
    decimal CostUsd,
    decimal CostCapUsd,
    DateTimeOffset DeadlineAt,
    string? PartialResult,
    bool Idempotent);

/// <summary>
/// What <c>list_tasks</c> may return. No instruction and no result.
/// </summary>
public sealed record AgentTaskListItem(
    Guid Id,
    string Title,
    string Kind,
    string State,
    string? FailureReason,
    decimal CostUsd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>A terminal row whose finish notice has not been disarmed yet.</summary>
public sealed record ArmedAgentTaskNotice(
    Guid Id,
    Guid UserId,
    Guid ConversationId,
    string Title,
    string State,
    string? FailureReason,
    decimal CostUsd);

/// <summary>Fenced close. Applied only while this worker still owns a <c>working</c> row.</summary>
public sealed record AgentTaskClose(string State, string? FailureReason);

/// <summary>Activity page: the user's tasks and the latest audited tools, without tool arguments.</summary>
public sealed record ActivityPage(
    IReadOnlyList<ActivityTaskItem> Tasks,
    IReadOnlyList<ActivityToolItem> Tools);

/// <summary>One task on the activity page. The partial is already trimmed.</summary>
public sealed record ActivityTaskItem(
    Guid Id,
    string Kind,
    string State,
    string? FailureReason,
    decimal CostUsd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? Partial);

/// <summary>One audited tool call. No arguments and no result.</summary>
public sealed record ActivityToolItem(
    string Name,
    string Extension,
    string Outcome,
    DateTimeOffset At);

/// <summary>
/// Durable task table. <c>now</c> is always supplied by the caller from <see cref="TimeProvider"/>;
/// implementations must not call the database clock.
/// </summary>
public interface IAgentTaskStore
{
    /// <summary>
    /// Inserts a task. An existing idempotency key is returned as-is and a finished row is not revived.
    /// </summary>
    public ValueTask<AgentTaskRow> InsertAsync(AgentTaskInsert spec, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Claims one <c>submitted</c> task with <c>FOR UPDATE SKIP LOCKED</c>.
    /// </summary>
    public ValueTask<ClaimedAgentTask?> ClaimAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken ct);

    /// <summary>Extends the lease when this worker still owns a <c>working</c> row.</summary>
    public ValueTask<bool> RenewLeaseAsync(
        Guid id,
        string workerId,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken ct);

    /// <summary>
    /// Applies the lease rule to expired <c>working</c> rows. Does not clear <c>partial_result</c>
    /// and does not publish: the caller publishes armed terminal rows.
    /// </summary>
    public ValueTask<int> ReapExpiredAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Closes a <c>working</c> row owned by <paramref name="workerId"/>. False when the fence misses.
    /// </summary>
    public ValueTask<bool> CompleteAsync(
        Guid id,
        string workerId,
        AgentTaskClose close,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Sets <c>canceled</c> when the row belongs to <paramref name="userId"/> and is still open.
    /// Returns null when nothing changed, without saying whether the id exists.
    /// </summary>
    public ValueTask<ArmedAgentTaskNotice?> CancelAsync(
        Guid id,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Writes <c>partial_result</c> for the worker that still owns the row.</summary>
    public ValueTask SavePartialAsync(
        Guid id,
        string workerId,
        string partialResult,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Stores <paramref name="externalTaskId"/> when it is free. False when another row already has it.
    /// </summary>
    public ValueTask<bool> AttachExternalTaskAsync(
        Guid id,
        string externalTaskId,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Adds <paramref name="amount"/> to the cost while this worker still owns the row.</summary>
    public ValueTask<bool> AddCostAsync(
        Guid id,
        string workerId,
        decimal amount,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Tasks of one user, without the instruction or the partial.</summary>
    public ValueTask<IReadOnlyList<AgentTaskListItem>> ListForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Terminal rows that still need the finish notice.</summary>
    public ValueTask<IReadOnlyList<ArmedAgentTaskNotice>> ListArmedNoticesAsync(CancellationToken ct);

    /// <summary>Clears <c>notice_armed</c> after the finish notice was published.</summary>
    public ValueTask DisarmNoticeAsync(Guid id, DateTimeOffset now, CancellationToken ct);

    /// <summary>Tasks of the user and the latest tool calls from their conversations.</summary>
    public ValueTask<ActivityPage> GetActivityAsync(Guid userId, CancellationToken ct);
}
