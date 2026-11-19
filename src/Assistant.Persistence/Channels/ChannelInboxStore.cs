using Assistant.Abstractions.Channels;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Assistant.Persistence.Channels;

internal sealed class ChannelInboxStore(AssistantDbContext db, TimeProvider clock) : IChannelInboxStore
{
    public async ValueTask<long?> TryInsertAsync(
        InboundEnvelope envelope,
        CancellationToken ct)
    {
        var sql = """
            INSERT INTO channel_inbox (channel, external_id, external_peer_id, received_at, payload, status, attempts)
            VALUES ($1, $2, $3, $4, $5, 'received', 0)
            ON CONFLICT (channel, external_id) DO NOTHING
            RETURNING id
            """;

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter { Value = envelope.Channel.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = envelope.ExternalId });
        command.Parameters.Add(new NpgsqlParameter { Value = envelope.ExternalPeerId });
        command.Parameters.Add(new NpgsqlParameter { Value = envelope.ReceivedAt });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = envelope.RawPayloadJson,
        });

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(ct);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is long id ? id : null;
    }

    public async ValueTask<InboundItem?> FindAsync(long inboxId, CancellationToken ct)
    {
        var entity = await db.ChannelInbox
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == inboxId, ct);
        return entity is null ? null : ToItem(entity);
    }

    public ValueTask<IReadOnlyList<InboundItem>> ListRecoverableAsync(int limit, CancellationToken ct)
        => ListRecoverableAsync(0, limit, ct);

    public async ValueTask<IReadOnlyList<InboundItem>> ListRecoverableAsync(
        long afterId,
        int limit,
        CancellationToken ct)
    {
        var staleThreshold = clock.GetUtcNow().AddMinutes(-5);

        var entities = await db.ChannelInbox
            .AsNoTracking()
            .Where(i => i.Id > afterId
                        && (i.Status == "received"
                            || (i.Status == "processing" && i.ReceivedAt < staleThreshold)))
            .OrderBy(i => i.Id)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(ToItem).ToList();
    }

    public async ValueTask MarkAsync(
        long inboxId,
        string status,
        string? error,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.ChannelInbox
            .Where(i => i.Id == inboxId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.Status, status)
                    .SetProperty(i => i.LastError, error)
                    .SetProperty(i => i.ProcessedAt, status is "done" or "ignored" or "failed" ? now : null)
                    .SetProperty(i => i.Payload, i => status == "done" || status == "ignored" || status == "failed" ? null : i.Payload)
                    .SetProperty(i => i.Attempts, i => i.Attempts + (status == "processing" ? 1 : 0)),
                ct);
    }

    public async ValueTask<int> PurgeAsync(
        ChannelId channel,
        TimeSpan retention,
        CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - retention;

        return await db.ChannelInbox
            .Where(i => i.Channel == channel.Value && i.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static InboundItem ToItem(ChannelInboxEntity entity)
        => new(
            entity.Id,
            new InboundEnvelope(
                new ChannelId(entity.Channel),
                entity.ExternalId,
                entity.ExternalPeerId,
                null,
                entity.ReceivedAt,
                entity.Payload ?? "{}"),
            entity.Attempts);
}
