using System.Data;
using System.Data.Common;
using Assistant.Abstractions.Tasks;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Assistant.Persistence.Tasks;

/// <summary>
/// PostgreSQL task table. Every timestamp is a bound parameter from the caller.
/// The SQL does not call <c>now()</c>.
/// </summary>
internal sealed class AgentTaskStore(AssistantDbContext db) : IAgentTaskStore
{
    public const int ActivityPartialMaxCharacters = 2000;

    public const int ActivityToolLimit = 50;

    public async ValueTask<AgentTaskRow> InsertAsync(AgentTaskInsert spec, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Validate(spec);
        var id = Guid.CreateVersion7();
        var completedAt = AgentTaskStatuses.IsTerminal(spec.State) ? now : (DateTimeOffset?)null;
        const string Sql = """
            INSERT INTO agent_tasks
                (id, user_id, conversation_id, origin_turn_id, usage_turn_id, idempotency_key,
                 kind, title, instruction, state, failure_reason, cost_usd, cost_cap_usd, deadline_at,
                 idempotent, restart_count, notice_armed, completed_at, created_at, updated_at)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, 0, $16, $17, $18, $18)
            ON CONFLICT (idempotency_key) DO NOTHING
            RETURNING id
            """;

        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.UserId });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.ConversationId });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.OriginTurnId });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)spec.UsageTurnId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.IdempotencyKey });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.Kind });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.Title });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.Instruction });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.State });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)spec.FailureReason ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.CostUsd });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.CostCapUsd });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.DeadlineAt });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.Idempotent });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.ArmNotice });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)completedAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = now });

        var inserted = await command.ExecuteScalarAsync(ct);
        if (inserted is Guid created)
        {
            return new AgentTaskRow(
                created,
                spec.UserId,
                spec.ConversationId,
                spec.OriginTurnId,
                spec.UsageTurnId,
                spec.IdempotencyKey,
                spec.Kind,
                spec.Title,
                spec.Instruction,
                spec.State,
                spec.FailureReason,
                null,
                spec.CostUsd,
                spec.CostCapUsd,
                spec.DeadlineAt,
                spec.Idempotent,
                0,
                null,
                spec.ArmNotice,
                null,
                null,
                now,
                now,
                completedAt);
        }

        return await FindByKeyAsync(spec.IdempotencyKey, ct)
            ?? throw new InvalidOperationException(
                "An idempotent task conflict did not resolve to an existing row.");
    }

    public async ValueTask<ClaimedAgentTask?> ClaimAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        const string Sql = """
            UPDATE agent_tasks AS t
            SET state = 'working',
                locked_by = $1,
                locked_until = $2,
                updated_at = $3
            WHERE t.id IN (
                SELECT id FROM agent_tasks
                WHERE state = 'submitted'
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING t.id
            """;

        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = now.Add(lease) });
        command.Parameters.Add(new NpgsqlParameter { Value = now });
        var claimed = await command.ExecuteScalarAsync(ct);
        if (claimed is not Guid id)
        {
            return null;
        }

        var row = await LoadAsync(id, ct);
        return new ClaimedAgentTask(
            row.Id,
            row.UserId,
            row.ConversationId,
            row.Kind,
            row.Title,
            row.Instruction,
            row.CostUsd,
            row.CostCapUsd,
            row.DeadlineAt,
            row.PartialResult,
            row.Idempotent);
    }

    public async ValueTask<bool> RenewLeaseAsync(
        Guid id,
        string workerId,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken ct)
    {
        var affected = await Owned(id, workerId).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(task => task.LockedUntil, now.Add(lease))
                .SetProperty(task => task.UpdatedAt, now),
            ct);
        return affected == 1;
    }

    public async ValueTask<int> ReapExpiredAsync(DateTimeOffset now, CancellationToken ct)
    {
        // A single statement so a claim of a still-valid lease cannot interleave.
        // partial_result is not in the SET list.
        const string Sql = """
            UPDATE agent_tasks
            SET state = CASE
                    WHEN idempotent AND restart_count = 0 THEN 'submitted'
                    ELSE 'failed'
                END,
                failure_reason = CASE
                    WHEN idempotent AND restart_count = 0 THEN NULL
                    ELSE 'interrupted'
                END,
                restart_count = CASE
                    WHEN idempotent AND restart_count = 0 THEN restart_count + 1
                    ELSE restart_count
                END,
                locked_by = NULL,
                locked_until = NULL,
                completed_at = CASE
                    WHEN idempotent AND restart_count = 0 THEN NULL
                    ELSE $1
                END,
                updated_at = $1
            WHERE state = 'working' AND locked_until < $1
            """;

        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = now });
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<bool> CompleteAsync(
        Guid id,
        string workerId,
        AgentTaskClose close,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(close);
        var affected = await Owned(id, workerId).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(task => task.State, close.State)
                .SetProperty(task => task.FailureReason, close.FailureReason)
                .SetProperty(task => task.LockedBy, (string?)null)
                .SetProperty(task => task.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(task => task.CompletedAt, now)
                .SetProperty(task => task.UpdatedAt, now),
            ct);
        return affected == 1;
    }

    public async ValueTask<ArmedAgentTaskNotice?> CancelAsync(
        Guid id,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var row = await db.AgentTasks.AsNoTracking()
            .Where(task => task.Id == id && task.UserId == userId)
            .Select(task => new { task.State, task.Title, task.ConversationId, task.FailureReason, task.CostUsd })
            .FirstOrDefaultAsync(ct);
        if (row is null || !AgentTaskStatuses.IsCancellable(row.State))
        {
            return null;
        }

        var affected = await db.AgentTasks
            .Where(task => task.Id == id
                && task.UserId == userId
                && (task.State == AgentTaskStatuses.Submitted
                    || task.State == AgentTaskStatuses.Working
                    || task.State == AgentTaskStatuses.InputRequired))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(task => task.State, AgentTaskStatuses.Canceled)
                    .SetProperty(task => task.FailureReason, (string?)null)
                    .SetProperty(task => task.LockedBy, (string?)null)
                    .SetProperty(task => task.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(task => task.CompletedAt, now)
                    .SetProperty(task => task.UpdatedAt, now),
                ct);
        if (affected != 1)
        {
            return null;
        }

        return new ArmedAgentTaskNotice(
            id,
            userId,
            row.ConversationId,
            row.Title,
            AgentTaskStatuses.Canceled,
            null,
            row.CostUsd);
    }

    public async ValueTask SavePartialAsync(
        Guid id,
        string workerId,
        string partialResult,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(partialResult);
        await Owned(id, workerId).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(task => task.PartialResult, partialResult)
                .SetProperty(task => task.UpdatedAt, now),
            ct);
    }

    public async ValueTask<bool> AttachExternalTaskAsync(
        Guid id,
        string externalTaskId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalTaskId);
        if (externalTaskId.Length > 200)
        {
            throw new ArgumentException("The external task id is longer than 200 characters.", nameof(externalTaskId));
        }

        const string Sql = """
            UPDATE agent_tasks AS t
            SET external_task_id = $1,
                updated_at = $2
            WHERE t.id = $3
              AND (t.external_task_id IS NULL OR t.external_task_id = $1)
              AND NOT EXISTS (
                  SELECT 1 FROM agent_tasks AS other
                  WHERE other.external_task_id = $1 AND other.id <> $3)
            """;

        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = externalTaskId });
        command.Parameters.Add(new NpgsqlParameter { Value = now });
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async ValueTask<bool> AddCostAsync(
        Guid id,
        string workerId,
        decimal amount,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await Owned(id, workerId).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(task => task.CostUsd, task => task.CostUsd + amount)
                .SetProperty(task => task.UpdatedAt, now),
            ct);
        return affected == 1;
    }

    public async ValueTask<IReadOnlyList<AgentTaskListItem>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        return await db.AgentTasks.AsNoTracking()
            .Where(task => task.UserId == userId)
            .OrderByDescending(task => task.CreatedAt)
            .Select(task => new AgentTaskListItem(
                task.Id,
                task.Title,
                task.Kind,
                task.State,
                task.FailureReason,
                task.CostUsd,
                task.CreatedAt,
                task.UpdatedAt,
                task.CompletedAt))
            .ToListAsync(ct);
    }

    public async ValueTask<IReadOnlyList<ArmedAgentTaskNotice>> ListArmedNoticesAsync(CancellationToken ct)
    {
        return await db.AgentTasks.AsNoTracking()
            .Where(task => task.NoticeArmed
                && (task.State == AgentTaskStatuses.Completed
                    || task.State == AgentTaskStatuses.Failed
                    || task.State == AgentTaskStatuses.Canceled
                    || task.State == AgentTaskStatuses.Rejected))
            .Select(task => new ArmedAgentTaskNotice(
                task.Id,
                task.UserId,
                task.ConversationId,
                task.Title,
                task.State,
                task.FailureReason,
                task.CostUsd))
            .ToListAsync(ct);
    }

    public async ValueTask DisarmNoticeAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await db.AgentTasks
            .Where(task => task.Id == id && task.NoticeArmed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(task => task.NoticeArmed, false)
                    .SetProperty(task => task.UpdatedAt, now),
                ct);
    }

    public async ValueTask<ActivityPage> GetActivityAsync(Guid userId, CancellationToken ct)
    {
        var taskRows = await db.AgentTasks.AsNoTracking()
            .Where(task => task.UserId == userId)
            .OrderByDescending(task => task.CreatedAt)
            .Select(task => new
            {
                task.Id,
                task.Kind,
                task.State,
                task.FailureReason,
                task.CostUsd,
                task.CreatedAt,
                task.UpdatedAt,
                task.CompletedAt,
                task.PartialResult,
            })
            .ToListAsync(ct);
        var tasks = taskRows
            .Select(task => new ActivityTaskItem(
                task.Id,
                task.Kind,
                task.State,
                task.FailureReason,
                task.CostUsd,
                task.CreatedAt,
                task.UpdatedAt,
                task.CompletedAt,
                TrimPartial(task.PartialResult)))
            .ToList();

        var tools = await (
            from invocation in db.ToolInvocations.AsNoTracking()
            join message in db.Messages.AsNoTracking() on invocation.MessageId equals message.Id
            join conversation in db.Conversations.AsNoTracking() on message.ConversationId equals conversation.Id
            where conversation.UserId == userId
            orderby invocation.CreatedAt descending
            select new ActivityToolItem(
                invocation.ToolName,
                invocation.ExtensionName,
                invocation.PermissionOutcome,
                invocation.CreatedAt))
            .Take(ActivityToolLimit)
            .ToListAsync(ct);

        return new ActivityPage(tasks, tools);
    }

    private static string? TrimPartial(string? partial)
    {
        if (partial is null || partial.Length <= ActivityPartialMaxCharacters)
        {
            return partial;
        }

        return partial[..ActivityPartialMaxCharacters];
    }

    private IQueryable<AgentTaskEntity> Owned(Guid id, string workerId)
        => db.AgentTasks.Where(task =>
            task.Id == id && task.LockedBy == workerId && task.State == AgentTaskStatuses.Working);

    private async Task<AgentTaskRow> LoadAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.AgentTasks.AsNoTracking().SingleAsync(task => task.Id == id, ct);
        return ToRow(entity);
    }

    private async Task<AgentTaskRow?> FindByKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        var entity = await db.AgentTasks.AsNoTracking()
            .SingleOrDefaultAsync(task => task.IdempotencyKey == idempotencyKey, ct);
        return entity is null ? null : ToRow(entity);
    }

    private static AgentTaskRow ToRow(AgentTaskEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.ConversationId,
            entity.OriginTurnId,
            entity.UsageTurnId,
            entity.IdempotencyKey,
            entity.Kind,
            entity.Title,
            entity.Instruction,
            entity.State,
            entity.FailureReason,
            entity.PartialResult,
            entity.CostUsd,
            entity.CostCapUsd,
            entity.DeadlineAt,
            entity.Idempotent,
            entity.RestartCount,
            entity.ExternalTaskId,
            entity.NoticeArmed,
            entity.LockedBy,
            entity.LockedUntil,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.CompletedAt);

    private static void Validate(AgentTaskInsert spec)
    {
        if (string.IsNullOrWhiteSpace(spec.IdempotencyKey) || spec.IdempotencyKey.Length > 200)
        {
            throw new ArgumentException("The idempotency key must be 1 to 200 characters.", nameof(spec));
        }

        if (string.IsNullOrWhiteSpace(spec.Kind) || spec.Kind.Length > 64)
        {
            throw new ArgumentException("The task kind must be 1 to 64 characters.", nameof(spec));
        }

        if (string.IsNullOrWhiteSpace(spec.Title) || spec.Title.Length > 60)
        {
            throw new ArgumentException("The task title must be 1 to 60 characters.", nameof(spec));
        }

        if (spec.Instruction.Length is < 1 or > 8000)
        {
            throw new ArgumentException("The instruction must be 1 to 8000 characters.", nameof(spec));
        }

        if (spec.State is not (
            AgentTaskStatuses.Submitted
            or AgentTaskStatuses.Working
            or AgentTaskStatuses.InputRequired
            or AgentTaskStatuses.AuthRequired
            or AgentTaskStatuses.Completed
            or AgentTaskStatuses.Failed
            or AgentTaskStatuses.Canceled
            or AgentTaskStatuses.Rejected))
        {
            throw new ArgumentException("The task state is not a wire state.", nameof(spec));
        }

        if (spec.CostUsd < 0)
        {
            throw new ArgumentException("The task cost cannot be negative.", nameof(spec));
        }

        if (spec.FailureReason is not null
            && spec.FailureReason is not (
                AgentTaskFailureReasons.Interrupted
                or AgentTaskFailureReasons.ProviderBudget
                or AgentTaskFailureReasons.Network
                or AgentTaskFailureReasons.CostCap
                or AgentTaskFailureReasons.Deadline
                or AgentTaskFailureReasons.UnknownKind
                or AgentTaskFailureReasons.WorkerError))
        {
            throw new ArgumentException("The failure reason is not a wire reason.", nameof(spec));
        }
    }

    private async Task<DbCommand> CreateCommandAsync(string sql, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (db.Database.CurrentTransaction is { } transaction)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        return command;
    }
}
