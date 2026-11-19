using System.Data;
using Assistant.Abstractions.Webhooks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Assistant.Persistence.Webhooks;

internal sealed class InboundWebhookDeliveryStore(AssistantDbContext db) : IInboundWebhookDeliveryStore
{
    public async ValueTask<bool> TryRecordAsync(
        string source,
        string deliveryId,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        if (source.Length > InboundWebhookLimits.MaxSourceLength
            || deliveryId.Length > InboundWebhookLimits.MaxDeliveryIdLength)
        {
            throw new ArgumentException("The webhook source or delivery id is longer than the table allows.");
        }

        const string Sql = """
            INSERT INTO inbound_webhook_deliveries (source, delivery_id, received_at)
            VALUES ($1, $2, $3)
            ON CONFLICT DO NOTHING
            RETURNING source
            """;

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = Sql;
        command.Parameters.Add(new NpgsqlParameter { Value = source });
        command.Parameters.Add(new NpgsqlParameter { Value = deliveryId });
        command.Parameters.Add(new NpgsqlParameter { Value = receivedAt });

        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync(ct);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is string;
    }

    public async ValueTask<int> PurgeAsync(DateTimeOffset receivedBefore, CancellationToken ct)
        => await db.InboundWebhookDeliveries
            .Where(delivery => delivery.ReceivedAt < receivedBefore)
            .ExecuteDeleteAsync(ct);
}
