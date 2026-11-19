using System.Text;
using Assistant.Core.Memory;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Assistant.Persistence.Memory;

internal sealed class MemoryStore(AssistantDbContext db, TimeProvider clock) : IMemoryStore
{
    private const int PurgeBatchSize = 32;
    public async ValueTask<MemoryItem> UpsertAsync(UpsertMemoryRequest request, CancellationToken ct)
    {
        EnsureLength(request.Embedding, nameof(request));

        MemoryEntity entity;
        if (request.Id is null)
        {
            entity = new MemoryEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                ValidPeriod = OpenPeriod(clock.GetUtcNow()),
            };
            db.Memories.Add(entity);
        }
        else
        {
            entity = await db.Memories
                    .FirstOrDefaultAsync(m => m.Id == request.Id && m.UserId == request.UserId, ct)
                ?? throw new InvalidOperationException(
                    "The memory was not found or is not an active row for this user.");
        }

        entity.Kind = request.Kind;
        entity.Content = request.Content;
        entity.SourceConversationId = request.SourceConversationId;
        entity.DueAt = request.DueAt;
        entity.Embedding = new Vector(request.Embedding);
        entity.EmbeddingModel = request.EmbeddingModel;
        entity.WriteProvenance = request.WriteProvenance;
        entity.Pinned = request.Pinned;
        entity.IndexTerms = request.IndexTerms;
        entity.DerivedFrom = CopyIds(request.DerivedFrom);

        await db.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async ValueTask<MemoryItem?> FindAsync(Guid memoryId, Guid userId, CancellationToken ct)
    {
        var row = await ProjectRows(db.Memories.Where(m => m.Id == memoryId && m.UserId == userId))
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToItem(row);
    }

    public async ValueTask ForgetAsync(Guid memoryId, Guid userId, CancellationToken ct)
    {
        var entity = await db.Memories
            .FirstOrDefaultAsync(m => m.Id == memoryId && m.UserId == userId, ct);

        if (entity is null)
        {
            return;
        }

        entity.DeletedAt = clock.GetUtcNow();
        entity.DeletionReason = ReasonFor(entity.WriteProvenance);
        await db.SaveChangesAsync(ct);
    }

    public async ValueTask<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var userIds = await db.Memories
            .IgnoreQueryFilters()
            .Where(m => m.DeletedAt != null && m.DeletedAt <= cutoff && m.Content != null)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        var purged = 0;
        foreach (var userId in userIds)
        {
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.Memories
                    .IgnoreQueryFilters()
                    .Where(m =>
                        m.UserId == userId
                        && m.DeletedAt != null
                        && m.DeletedAt <= cutoff
                        && m.Content != null)
                    .OrderBy(m => m.DeletedAt)
                    .Take(PurgeBatchSize)
                    .ToListAsync(ct);

                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var entity in batch)
                {
                    entity.DeletionReason ??= ReasonFor(entity.WriteProvenance);
                    entity.Content = null;
                    entity.IndexTerms = null;
                    entity.Embedding = null;
                }

                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                purged += batch.Count;
            }
        }

        return purged;
    }

    public async ValueTask<MemoryRestoreStatus> RestoreAsync(
        Guid memoryId, Guid userId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var entity = await db.Memories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == memoryId && m.UserId == userId, ct);

        if (entity?.DeletedAt is null)
        {
            return MemoryRestoreStatus.NotFound;
        }

        if (entity.DeletedAt <= cutoff || entity.Content is null || entity.Embedding is null)
        {
            return MemoryRestoreStatus.Expired;
        }

        entity.DeletedAt = null;
        entity.DeletionReason = null;
        await db.SaveChangesAsync(ct);
        return MemoryRestoreStatus.Restored;
    }

    public async ValueTask<IReadOnlyList<ScoredMemory>> SearchAsync(
        SearchMemoryRequest request, CancellationToken ct)
    {
        EnsureLength(request.Query, nameof(request));

        if (db.Database.CurrentTransaction is null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var owned = await QueryHitsAsync(request, ct);
            await transaction.CommitAsync(ct);
            return owned;
        }

        return await QueryHitsAsync(request, ct);
    }

    public async ValueTask<IReadOnlyList<MemoryItem>> ListAsync(
        Guid userId, int skip, int take, CancellationToken ct)
    {
        var asOf = AsOfUtc();
        var rows = await ProjectRows(
                db.Memories.Where(m =>
                        m.UserId == userId
                        && m.SupersededBy == null
                        && m.ValidPeriod.Contains(asOf))
                    .OrderByDescending(m => m.CreatedAt)
                    .Skip(skip)
                    .Take(take))
            .ToListAsync(ct);
        return rows.Select(ToItem).ToList();
    }

    public async ValueTask<MemoryJourney> ListJourneyAsync(Guid userId, CancellationToken ct)
    {
        var asOf = AsOfUtc();
        var rows = await db.Memories
            .Where(m => m.UserId == userId)
            .IgnoreQueryFilters()
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new JourneyRow(
                m.Id,
                m.UserId,
                m.Kind,
                m.Content,
                m.CreatedAt,
                m.WriteProvenance,
                m.Pinned,
                m.IndexTerms,
                m.ValidPeriod,
                m.SupersededBy,
                m.DeletedAt))
            .ToListAsync(ct);

        var current = new List<MemoryJourneyItem>();
        var replaced = new List<MemoryJourneyItem>();
        var forgotten = new List<MemoryJourneyItem>();
        foreach (var row in rows)
        {
            if (row.DeletedAt is not null)
            {
                var tombstone = row.Content is null;
                forgotten.Add(ToJourneyItem(
                    row,
                    tombstone ? null : row.Content,
                    tombstone ? null : row.IndexTerms));
                continue;
            }

            if (row.SupersededBy is not null)
            {
                replaced.Add(ToJourneyItem(row, row.Content, row.IndexTerms));
                continue;
            }

            if (ContainsInstant(row.ValidPeriod, asOf))
            {
                current.Add(ToJourneyItem(row, row.Content, row.IndexTerms));
            }
        }

        return new MemoryJourney(current, replaced, forgotten);
    }

    public async ValueTask<IReadOnlyList<MemoryItem>> ListPinnedAsync(Guid userId, CancellationToken ct)
    {
        var asOf = AsOfUtc();
        var rows = await ProjectRows(
                db.Memories.Where(m =>
                        m.UserId == userId
                        && m.Pinned
                        && m.SupersededBy == null
                        && m.ValidPeriod.Contains(asOf))
                    .OrderBy(m => m.CreatedAt))
            .ToListAsync(ct);
        return rows.Select(ToItem).ToList();
    }

    public async ValueTask<IReadOnlyList<MemoryItem>> ListCitingAsync(
        Guid userId, Guid evidenceId, CancellationToken ct)
    {
        var rows = await ProjectRows(
                db.Memories.Where(m =>
                    m.UserId == userId && m.DerivedFrom != null && m.DerivedFrom.Contains(evidenceId)))
            .ToListAsync(ct);
        return rows.Select(ToItem).ToList();
    }

    public async ValueTask<IReadOnlyList<Guid>> ListUserIdsAsync(CancellationToken ct)
        => await db.Memories
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

    public async ValueTask<MemoryItem?> ReplaceAsync(
        Guid replacesId, UpsertMemoryRequest request, CancellationToken ct)
    {
        EnsureLength(request.Embedding, nameof(request));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var previous = await db.Memories
            .FirstOrDefaultAsync(m => m.Id == replacesId && m.UserId == request.UserId, ct);
        if (previous is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var entity = new MemoryEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Kind = request.Kind,
            Content = request.Content,
            SourceConversationId = request.SourceConversationId,
            DueAt = request.DueAt,
            Embedding = new Vector(request.Embedding),
            EmbeddingModel = request.EmbeddingModel,
            WriteProvenance = request.WriteProvenance,
            Pinned = request.Pinned,
            IndexTerms = request.IndexTerms,
            DerivedFrom = CopyIds(request.DerivedFrom),
            ValidPeriod = OpenPeriod(now),
        };
        previous.ValidPeriod = ClosePeriod(previous.ValidPeriod, now);
        previous.SupersededBy = entity.Id;
        db.Memories.Add(entity);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDomain(entity);
    }

    public async ValueTask<MemorySupersedeStatus> SupersedeAsync(
        Guid previousId,
        Guid successorId,
        Guid userId,
        DateTimeOffset expectedUpdatedAt,
        DateTimeOffset at,
        CancellationToken ct)
    {
        if (previousId == successorId)
        {
            return MemorySupersedeStatus.Missing;
        }

        var previous = await db.Memories
            .FirstOrDefaultAsync(m => m.Id == previousId && m.UserId == userId, ct);
        if (previous is null)
        {
            return MemorySupersedeStatus.Missing;
        }

        var successorExists = await db.Memories
            .AnyAsync(m => m.Id == successorId && m.UserId == userId, ct);
        if (!successorExists)
        {
            return MemorySupersedeStatus.Missing;
        }

        if (!SameMicrosecond(previous.UpdatedAt, expectedUpdatedAt))
        {
            return MemorySupersedeStatus.VersionMismatch;
        }

        previous.ValidPeriod = ClosePeriod(previous.ValidPeriod, at);
        previous.SupersededBy = successorId;
        await db.SaveChangesAsync(ct);
        return MemorySupersedeStatus.Done;
    }

    public async ValueTask<bool> ClearSupersessionAsync(
        Guid previousId, Guid userId, Guid successorId, CancellationToken ct)
    {
        var previous = await db.Memories
            .FirstOrDefaultAsync(m => m.Id == previousId && m.UserId == userId, ct);
        if (previous is null || previous.SupersededBy != successorId)
        {
            return false;
        }

        previous.ValidPeriod = ReopenPeriod(previous.ValidPeriod);
        previous.SupersededBy = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async ValueTask<bool> SetPinnedAsync(
        Guid memoryId, Guid userId, bool pinned, CancellationToken ct)
    {
        var entity = await db.Memories
            .FirstOrDefaultAsync(m => m.Id == memoryId && m.UserId == userId, ct);
        if (entity is null)
        {
            return false;
        }

        entity.Pinned = pinned;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<IReadOnlyList<ScoredMemory>> QueryHitsAsync(
        SearchMemoryRequest request, CancellationToken ct)
    {
        await using (var set = CreateCommand())
        {
            set.CommandText = "SET LOCAL hnsw.iterative_scan = 'strict_order'";
            await set.ExecuteNonQueryAsync(ct);
        }

        await using var command = CreateCommand();
        command.Parameters.Add(new NpgsqlParameter("query", new Vector(request.Query)));
        command.Parameters.Add(new NpgsqlParameter("userId", request.UserId));
        command.Parameters.Add(new NpgsqlParameter("maxDistance", 1d - request.MinScore));
        command.Parameters.Add(new NpgsqlParameter("topK", request.TopK));

        var where = new StringBuilder("""
            user_id = @userId
            AND deleted_at IS NULL
            """);
        if (!request.IncludeHistory)
        {
            where.Append("""

                AND superseded_by IS NULL
                AND valid_period @> @asOf
                """);
            command.Parameters.Add(new NpgsqlParameter("asOf", clock.GetUtcNow()));
        }

        if (request.Kind is { } kind)
        {
            where.Append(" AND kind = @kind");
            command.Parameters.Add(new NpgsqlParameter("kind", kind.ToString().ToLowerInvariant()));
        }

        if (request.Since is { } since)
        {
            where.Append(" AND created_at >= @since");
            command.Parameters.Add(new NpgsqlParameter("since", since));
        }

        if (request.ExcludePinned)
        {
            where.Append(" AND pinned = false");
        }

        // Filters stay inside the CTE. The distance threshold stays outside so it
        // cannot starve the HNSW scan. iterative_scan is set on this transaction.
        command.CommandText = $"""
            WITH candidates AS MATERIALIZED (
                SELECT
                    id,
                    user_id,
                    kind,
                    content,
                    source_conversation_id,
                    due_at,
                    created_at,
                    updated_at,
                    write_provenance,
                    pinned,
                    index_terms,
                    derived_from,
                    superseded_by,
                    valid_period,
                    embedding <=> @query AS distance
                FROM memories
                WHERE {where}
            )
            SELECT
                id,
                user_id,
                kind,
                content,
                source_conversation_id,
                due_at,
                created_at,
                updated_at,
                write_provenance,
                pinned,
                index_terms,
                derived_from,
                superseded_by,
                CASE WHEN upper_inf(valid_period) THEN NULL ELSE upper(valid_period) END,
                distance
            FROM candidates
            WHERE distance <= @maxDistance
            ORDER BY distance
            LIMIT @topK
            """;

        var hits = new List<ScoredMemory>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            hits.Add(new ScoredMemory(ReadItem(reader), 1f - (float)reader.GetDouble(14)));
        }

        return hits;
    }

    private NpgsqlCommand CreateCommand()
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var command = connection.CreateCommand();
        if (db.Database.CurrentTransaction is { } current)
        {
            command.Transaction = (NpgsqlTransaction)current.GetDbTransaction();
        }

        return command;
    }

    private static MemoryItem ReadItem(NpgsqlDataReader reader)
    {
        List<Guid>? derived = reader.IsDBNull(11)
            ? null
            : reader.GetFieldValue<Guid[]>(11).ToList();

        return new MemoryItem(
            reader.GetGuid(0),
            reader.GetGuid(1),
            Enum.Parse<MemoryKind>(reader.GetString(2), ignoreCase: true),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : ReadTimestamp(reader, 5),
            ReadTimestamp(reader, 6),
            ReadTimestamp(reader, 7),
            ParseProvenance(reader.GetString(8)),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            derived,
            reader.IsDBNull(13) ? null : ReadTimestamp(reader, 13),
            reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetFieldValue<DateTime>(ordinal);
        if (value.Kind != DateTimeKind.Utc)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return new DateTimeOffset(value);
    }

    private static WriteProvenance ParseProvenance(string stored)
        => Enum.Parse<WriteProvenance>(
            stored.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);

    private static void EnsureLength(float[] embedding, string paramName)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding.Length != MemorySchema.EmbeddingDimensions)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                embedding.Length,
                $"Embedding length must be {MemorySchema.EmbeddingDimensions}.");
        }
    }

    private DateTime AsOfUtc()
        => DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Utc);

    private static NpgsqlRange<DateTime> OpenPeriod(DateTimeOffset start)
    {
        var lower = DateTime.SpecifyKind(start.UtcDateTime, DateTimeKind.Utc);
        return new NpgsqlRange<DateTime>(lower, true, false, default, false, true);
    }

    private static NpgsqlRange<DateTime> ClosePeriod(NpgsqlRange<DateTime> period, DateTimeOffset end)
    {
        var lower = period.LowerBound;
        if (lower.Kind != DateTimeKind.Utc)
        {
            lower = DateTime.SpecifyKind(lower, DateTimeKind.Utc);
        }

        var upper = DateTime.SpecifyKind(end.UtcDateTime, DateTimeKind.Utc);
        return new NpgsqlRange<DateTime>(
            lower,
            period.LowerBoundIsInclusive,
            period.LowerBoundInfinite,
            upper,
            upperBoundIsInclusive: false,
            upperBoundInfinite: false);
    }

    private static NpgsqlRange<DateTime> ReopenPeriod(NpgsqlRange<DateTime> period)
    {
        var lower = period.LowerBound;
        if (!period.LowerBoundInfinite && lower.Kind != DateTimeKind.Utc)
        {
            lower = DateTime.SpecifyKind(lower, DateTimeKind.Utc);
        }

        return new NpgsqlRange<DateTime>(
            lower,
            period.LowerBoundIsInclusive,
            period.LowerBoundInfinite,
            default,
            upperBoundIsInclusive: false,
            upperBoundInfinite: true);
    }

    /// <summary>
    /// PostgreSQL <c>timestamptz</c> keeps microseconds. A tracked entity can still
    /// hold the clock's 100-nanosecond ticks, so equality is at that resolution.
    /// </summary>
    private static bool SameMicrosecond(DateTimeOffset stored, DateTimeOffset expected)
        => stored.ToUniversalTime().Ticks / 10 == expected.ToUniversalTime().Ticks / 10;

    private static bool ContainsInstant(NpgsqlRange<DateTime> period, DateTime instant)
    {
        if (!period.LowerBoundInfinite)
        {
            var afterLower = period.LowerBoundIsInclusive
                ? instant >= period.LowerBound
                : instant > period.LowerBound;
            if (!afterLower)
            {
                return false;
            }
        }

        if (period.UpperBoundInfinite)
        {
            return true;
        }

        return period.UpperBoundIsInclusive
            ? instant <= period.UpperBound
            : instant < period.UpperBound;
    }

    private static DateTimeOffset? ValidUntil(NpgsqlRange<DateTime> period)
    {
        if (period.UpperBoundInfinite)
        {
            return null;
        }

        var upper = period.UpperBound;
        if (upper.Kind != DateTimeKind.Utc)
        {
            upper = DateTime.SpecifyKind(upper, DateTimeKind.Utc);
        }

        return new DateTimeOffset(upper);
    }

    private static string ReasonFor(WriteProvenance provenance)
        => provenance == WriteProvenance.Inferred ? "inferred" : "user";

    private static MemoryItem ToDomain(MemoryEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            entity.Kind,
            RequireContent(entity.Content),
            entity.SourceConversationId,
            entity.DueAt,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.WriteProvenance,
            entity.Pinned,
            entity.IndexTerms,
            entity.DerivedFrom,
            ValidUntil(entity.ValidPeriod),
            entity.SupersededBy);

    private static IQueryable<MemoryRow> ProjectRows(IQueryable<MemoryEntity> query)
        => query.Select(m => new MemoryRow(
            m.Id,
            m.UserId,
            m.Kind,
            m.Content,
            m.SourceConversationId,
            m.DueAt,
            m.CreatedAt,
            m.UpdatedAt,
            m.WriteProvenance,
            m.Pinned,
            m.IndexTerms,
            m.DerivedFrom,
            m.ValidPeriod,
            m.SupersededBy));

    private static MemoryJourneyItem ToJourneyItem(JourneyRow row, string? content, string? indexTerms)
        => new(
            row.Id,
            row.UserId,
            row.Kind,
            content,
            row.CreatedAt,
            row.WriteProvenance,
            row.Pinned,
            indexTerms,
            row.SupersededBy);

    private static MemoryItem ToItem(MemoryRow row)
        => new(
            row.Id,
            row.UserId,
            row.Kind,
            RequireContent(row.Content),
            row.SourceConversationId,
            row.DueAt,
            row.CreatedAt,
            row.UpdatedAt,
            row.WriteProvenance,
            row.Pinned,
            row.IndexTerms,
            row.DerivedFrom,
            ValidUntil(row.ValidPeriod),
            row.SupersededBy);

    private static string RequireContent(string? content)
        => content ?? throw new InvalidOperationException("An active memory has no content.");

    private static List<Guid>? CopyIds(IReadOnlyList<Guid>? ids)
        => ids is null ? null : [.. ids];

    private sealed record JourneyRow(
        Guid Id,
        Guid UserId,
        MemoryKind Kind,
        string? Content,
        DateTimeOffset CreatedAt,
        WriteProvenance WriteProvenance,
        bool Pinned,
        string? IndexTerms,
        NpgsqlRange<DateTime> ValidPeriod,
        Guid? SupersededBy,
        DateTimeOffset? DeletedAt);

    private sealed record MemoryRow(
        Guid Id,
        Guid UserId,
        MemoryKind Kind,
        string? Content,
        Guid? SourceConversationId,
        DateTimeOffset? DueAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        WriteProvenance WriteProvenance,
        bool Pinned,
        string? IndexTerms,
        List<Guid>? DerivedFrom,
        NpgsqlRange<DateTime> ValidPeriod,
        Guid? SupersededBy);
}
