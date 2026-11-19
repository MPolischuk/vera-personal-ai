using Assistant.Core.Memory;
using Assistant.Core.Notes;
using Assistant.Core.Research;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Pgvector;

namespace Assistant.Persistence.Notes;

internal sealed class NoteStore(AssistantDbContext db, TimeProvider clock) : INoteStore
{
    public async ValueTask<Note> CreateAsync(NewNote note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentException.ThrowIfNullOrWhiteSpace(note.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(note.Body);
        ArgumentNullException.ThrowIfNull(note.Sources);
        if (note.Sources.Count == 0)
        {
            throw new ArgumentException("A note needs at least one source.", nameof(note));
        }

        EnsureLength(note.Embedding, nameof(note));

        var entity = new NoteEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = note.UserId,
            Title = note.Title,
            Body = note.Body,
            Embedding = new Vector(note.Embedding),
        };

        for (var index = 0; index < note.Sources.Count; index++)
        {
            entity.Sources.Add(ToEntity(note.Sources[index], index));
        }

        db.Notes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToDomain(entity, []);
    }

    public async ValueTask<Note?> GetAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        var entity = await db.Notes
            .Include(note => note.Sources)
            .FirstOrDefaultAsync(note => note.Id == noteId && note.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var mapped = await MapAsync([entity], cancellationToken);
        return mapped[0];
    }

    public async ValueTask<IReadOnlyList<ScoredNote>> SearchAsync(
        SearchNotesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureLength(query.Embedding, nameof(query));
        if (query.TopK < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.TopK, "TopK must be at least 1.");
        }

        if (db.Database.CurrentTransaction is null)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var owned = await QueryHitsAsync(query, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return owned;
        }

        return await QueryHitsAsync(query, cancellationToken);
    }

    public async ValueTask<Note?> UpdateAsync(NoteUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var entity = await db.Notes
            .FirstOrDefaultAsync(
                note => note.Id == update.NoteId && note.UserId == update.UserId,
                cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (update.Title is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(update.Title);
            entity.Title = update.Title;
        }

        if (update.Body is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(update.Body);
            entity.Body = update.Body;
        }

        if (update.Embedding is not null)
        {
            EnsureLength(update.Embedding, nameof(update));
            entity.Embedding = new Vector(update.Embedding);
        }

        if (update.Verified)
        {
            entity.VerifiedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(update.UserId, update.NoteId, cancellationToken);
    }

    public async ValueTask<NoteLinkStatus> LinkAsync(
        Guid userId, Guid noteId, Guid otherNoteId, CancellationToken cancellationToken)
    {
        if (noteId == otherNoteId)
        {
            return NoteLinkStatus.SameNote;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var owned = await db.Notes
            .Where(note => note.UserId == userId && (note.Id == noteId || note.Id == otherNoteId))
            .Select(note => note.Id)
            .ToListAsync(cancellationToken);
        if (owned.Count != 2)
        {
            return NoteLinkStatus.NotFound;
        }

        var existing = await db.NoteLinks
            .Where(link =>
                (link.NoteId == noteId && link.OtherNoteId == otherNoteId)
                || (link.NoteId == otherNoteId && link.OtherNoteId == noteId))
            .ToListAsync(cancellationToken);

        if (!existing.Exists(link => link.NoteId == noteId && link.OtherNoteId == otherNoteId))
        {
            db.NoteLinks.Add(new NoteLinkEntity { NoteId = noteId, OtherNoteId = otherNoteId });
        }

        if (!existing.Exists(link => link.NoteId == otherNoteId && link.OtherNoteId == noteId))
        {
            db.NoteLinks.Add(new NoteLinkEntity { NoteId = otherNoteId, OtherNoteId = noteId });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoteLinkStatus.Linked;
    }

    public async ValueTask<bool> SoftDeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        var entity = await db.Notes
            .FirstOrDefaultAsync(note => note.Id == noteId && note.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyList<Note>> ListActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entities = await db.Notes
            .Include(note => note.Sources)
            .Where(note => note.UserId == userId)
            .OrderBy(note => note.CreatedAt)
            .ThenBy(note => note.Id)
            .ToListAsync(cancellationToken);
        return await MapAsync(entities, cancellationToken);
    }

    private async Task<IReadOnlyList<ScoredNote>> QueryHitsAsync(
        SearchNotesQuery query, CancellationToken cancellationToken)
    {
        await using (var set = CreateCommand())
        {
            set.CommandText = "SET LOCAL hnsw.iterative_scan = 'strict_order'";
            await set.ExecuteNonQueryAsync(cancellationToken);
        }

        var hits = new List<(Guid Id, double Distance)>();
        await using (var command = CreateCommand())
        {
            command.Parameters.Add(new NpgsqlParameter("query", new Vector(query.Embedding)));
            command.Parameters.Add(new NpgsqlParameter("userId", query.UserId));
            command.Parameters.Add(new NpgsqlParameter("topK", query.TopK));
            command.CommandText =
                """
                WITH candidates AS MATERIALIZED (
                    SELECT id, embedding <=> @query AS distance
                    FROM notes
                    WHERE user_id = @userId AND deleted_at IS NULL
                )
                SELECT id, distance
                FROM candidates
                ORDER BY distance
                LIMIT @topK
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add((reader.GetGuid(0), reader.GetDouble(1)));
            }
        }

        if (hits.Count == 0)
        {
            return [];
        }

        var ids = hits.Select(hit => hit.Id).ToArray();
        var entities = await db.Notes
            .Include(note => note.Sources)
            .Where(note => note.UserId == query.UserId && ids.Contains(note.Id))
            .ToListAsync(cancellationToken);
        var mapped = await MapAsync(entities, cancellationToken);
        var byId = mapped.ToDictionary(note => note.Id);

        var scored = new List<ScoredNote>(hits.Count);
        foreach (var hit in hits)
        {
            if (byId.TryGetValue(hit.Id, out var note))
            {
                scored.Add(new ScoredNote(note, 1f - (float)hit.Distance));
            }
        }

        return scored;
    }

    private async Task<IReadOnlyList<Note>> MapAsync(
        List<NoteEntity> entities, CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return [];
        }

        var ids = entities.Select(entity => entity.Id).ToArray();
        var links = await db.NoteLinks
            .Where(link => ids.Contains(link.NoteId))
            .Join(
                db.Notes,
                link => link.OtherNoteId,
                other => other.Id,
                (link, other) => new NeighborRow(link.NoteId, other.Id, other.Title))
            .ToListAsync(cancellationToken);

        var neighbors = links
            .GroupBy(link => link.NoteId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NoteNeighbor>)group
                    .OrderBy(link => link.Title, StringComparer.Ordinal)
                    .ThenBy(link => link.OtherId)
                    .Select(link => new NoteNeighbor(link.OtherId, link.Title))
                    .ToList());

        return entities
            .Select(entity => ToDomain(entity, neighbors.GetValueOrDefault(entity.Id) ?? []))
            .ToList();
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

    private static NoteSourceEntity ToEntity(NoteSource source, int ordinal)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Ordinal = ordinal,
            ClaimText = source.ClaimText,
            Fragment = source.Fragment,
            RetrievedAt = source.RetrievedAt,
            Url = source.Url,
            DocumentId = source.DocumentId,
            Page = source.Page,
            BboxLeft = source.Bbox?.Left,
            BboxTop = source.Bbox?.Top,
            BboxRight = source.Bbox?.Right,
            BboxBottom = source.Bbox?.Bottom,
        };

    private static Note ToDomain(NoteEntity entity, IReadOnlyList<NoteNeighbor> neighbors)
        => new(
            entity.Id,
            entity.UserId,
            entity.Title,
            entity.Body,
            entity.VerifiedAt,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.Sources
                .OrderBy(source => source.Ordinal)
                .Select(ToSource)
                .ToList(),
            neighbors);

    private static NoteSource ToSource(NoteSourceEntity source)
        => new(
            source.ClaimText,
            source.Fragment,
            source.RetrievedAt,
            source.Url,
            source.DocumentId,
            source.Page,
            ReadBbox(source));

    private static ResearchBoundingBox? ReadBbox(NoteSourceEntity source)
    {
        if (source.BboxLeft is not { } left
            || source.BboxTop is not { } top
            || source.BboxRight is not { } right
            || source.BboxBottom is not { } bottom)
        {
            return null;
        }

        return new ResearchBoundingBox(left, top, right, bottom);
    }

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

    private sealed record NeighborRow(Guid NoteId, Guid OtherId, string Title);
}
