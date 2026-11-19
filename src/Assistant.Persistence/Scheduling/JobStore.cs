using System.Data;
using System.Data.Common;
using Assistant.Abstractions.Scheduling;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Assistant.Persistence.Scheduling;

/// <summary>
/// PostgreSQL job table. Every timestamp is a bound parameter from the caller.
/// The SQL does not call <c>now()</c>.
/// </summary>
internal sealed class JobStore(AssistantDbContext db) : IJobStore
{
    private const int MaxReasonLength = 500;

    public async ValueTask<Guid> InsertAsync(JobSpec spec, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var id = Guid.CreateVersion7();
        const string Sql = """
            INSERT INTO scheduled_jobs
                (id, kind, payload, idempotency_key, run_at, status, attempts, max_attempts, created_at, updated_at)
            VALUES
                ($1, $2, $3, $4, $5, 'scheduled', 0, $6, $7, $7)
            ON CONFLICT DO NOTHING
            RETURNING id
            """;

        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.Kind });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = spec.PayloadJson,
        });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)spec.IdempotencyKey ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.RunAt });
        command.Parameters.Add(new NpgsqlParameter { Value = spec.MaxAttempts });
        command.Parameters.Add(new NpgsqlParameter { Value = now });

        var inserted = await command.ExecuteScalarAsync(ct);
        if (inserted is Guid created)
        {
            return created;
        }

        if (spec.IdempotencyKey is null)
        {
            throw new InvalidOperationException("Inserting a scheduled job did not return an id.");
        }

        return await FindByKeyAsync(spec.IdempotencyKey, ct)
            ?? throw new InvalidOperationException(
                "An idempotent schedule conflict did not resolve to an existing job.");
    }

    public async ValueTask<bool> CancelAsync(string idempotencyKey, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await db.ScheduledJobs
            .Where(job => job.IdempotencyKey == idempotencyKey && job.Status == JobStatuses.Scheduled)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Cancelled)
                    .SetProperty(job => job.CompletedAt, now)
                    .SetProperty(job => job.UpdatedAt, now)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null),
                ct);
        return affected == 1;
    }

    public async ValueTask<bool> RescheduleAsync(
        string idempotencyKey,
        DateTimeOffset runAt,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var affected = await db.ScheduledJobs
            .Where(job => job.IdempotencyKey == idempotencyKey && job.Status == JobStatuses.Scheduled)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.RunAt, runAt)
                    .SetProperty(job => job.UpdatedAt, now),
                ct);
        return affected == 1;
    }

    public async ValueTask<IReadOnlyList<ClaimedJob>> ClaimAsync(
        string workerId,
        DateTimeOffset now,
        int batchSize,
        TimeSpan lease,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (batchSize <= 0)
        {
            return [];
        }

        // §13.4: one statement, SKIP LOCKED, @now from the caller. locked_until is now+lease
        // computed here so the database clock is not involved.
        const string Sql = """
            UPDATE scheduled_jobs AS j
            SET status = 'running',
                locked_by = $1,
                locked_until = $2,
                attempts = j.attempts + 1,
                updated_at = $3
            WHERE j.id IN (
                SELECT id FROM scheduled_jobs
                WHERE status = 'scheduled' AND run_at <= $3
                ORDER BY run_at
                FOR UPDATE SKIP LOCKED
                LIMIT $4)
            RETURNING j.id, j.kind, j.payload, j.attempts, j.max_attempts, j.run_at
            """;

        await using var command = await CreateCommandAsync(Sql, ct);
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = now.Add(lease) });
        command.Parameters.Add(new NpgsqlParameter { Value = now });
        command.Parameters.Add(new NpgsqlParameter { Value = batchSize });

        var claimed = new List<ClaimedJob>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            claimed.Add(new ClaimedJob(
                reader.GetGuid(0),
                reader.GetString(1),
                ReadJson(reader, 2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        claimed.Sort(static (left, right) =>
        {
            var byTime = left.RunAt.CompareTo(right.RunAt);
            return byTime != 0 ? byTime : left.Id.CompareTo(right.Id);
        });
        return claimed;
    }

    public async ValueTask<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken ct)
    {
        var affected = await Owned(jobId, workerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.LockedUntil, now.Add(lease))
                    .SetProperty(job => job.UpdatedAt, now),
                ct);
        return affected == 1;
    }

    public async ValueTask<bool> CompleteAsync(
        Guid jobId,
        string workerId,
        JobCompletion completion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var affected = completion switch
        {
            JobCompletion.Succeeded => await Owned(jobId, workerId).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Succeeded)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(job => job.LastError, (string?)null)
                    .SetProperty(job => job.CompletedAt, now)
                    .SetProperty(job => job.UpdatedAt, now),
                ct),
            JobCompletion.Failed failed => await Owned(jobId, workerId).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Failed)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(job => job.LastError, Trim(failed.Reason))
                    .SetProperty(job => job.CompletedAt, now)
                    .SetProperty(job => job.UpdatedAt, now),
                ct),
            JobCompletion.Retry retry => await Owned(jobId, workerId).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Scheduled)
                    .SetProperty(job => job.RunAt, retry.RunAt)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(job => job.LastError, Trim(retry.Reason))
                    .SetProperty(job => job.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.UpdatedAt, now),
                ct),
            JobCompletion.Reschedule again => await Owned(jobId, workerId).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Scheduled)
                    .SetProperty(job => job.RunAt, again.RunAt)
                    .SetProperty(job => job.Attempts, job => job.Attempts > 0 ? job.Attempts - 1 : 0)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(job => job.LastError, (string?)null)
                    .SetProperty(job => job.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.UpdatedAt, now),
                ct),
            _ => throw new InvalidOperationException("Unknown job completion."),
        };
        return affected == 1;
    }

    public async ValueTask<int> ReapAsync(DateTimeOffset now, CancellationToken ct)
    {
        return await db.ScheduledJobs
            .Where(job => job.Status == JobStatuses.Running
                && job.LockedUntil != null
                && job.LockedUntil < now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobStatuses.Scheduled)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(job => job.UpdatedAt, now),
                ct);
    }

    public async ValueTask<int> PurgeAsync(
        DateTimeOffset now,
        TimeSpan succeededRetention,
        TimeSpan failedRetention,
        CancellationToken ct)
    {
        var succeededBefore = now - succeededRetention;
        var failedBefore = now - failedRetention;
        return await db.ScheduledJobs
            .Where(job =>
                ((job.Status == JobStatuses.Succeeded || job.Status == JobStatuses.Cancelled)
                    && job.CompletedAt != null
                    && job.CompletedAt < succeededBefore)
                || (job.Status == JobStatuses.Failed
                    && job.CompletedAt != null
                    && job.CompletedAt < failedBefore))
            .ExecuteDeleteAsync(ct);
    }

    private IQueryable<ScheduledJobEntity> Owned(Guid jobId, string workerId)
        => db.ScheduledJobs.Where(job =>
            job.Id == jobId && job.LockedBy == workerId && job.Status == JobStatuses.Running);

    private async Task<Guid?> FindByKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        await using var command = await CreateCommandAsync(
            "SELECT id FROM scheduled_jobs WHERE idempotency_key = $1",
            ct);
        command.Parameters.Add(new NpgsqlParameter { Value = idempotencyKey });
        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
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

    private static string ReadJson(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value as string ?? value.ToString() ?? "{}";
    }

    private static string Trim(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "error";
        }

        var trimmed = reason.Trim();
        return trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength];
    }
}
