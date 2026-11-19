using Assistant.Abstractions.Proactive;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Mcp;

internal sealed class McpOAuthCredentialStore(AssistantDbContext db) : IMcpOAuthTokenStore
{
    public async ValueTask SaveRefreshAsync(
        Guid userId, string serverId, string refreshProtected, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshProtected);
        if (refreshProtected.Length > 4000)
        {
            throw new ArgumentException("The protected refresh token is too long.", nameof(refreshProtected));
        }

        var existing = await db.McpOAuthCredentials
            .SingleOrDefaultAsync(
                credential => credential.UserId == userId && credential.ServerId == serverId,
                ct);
        if (existing is null)
        {
            db.McpOAuthCredentials.Add(new McpOAuthCredentialEntity
            {
                UserId = userId,
                ServerId = serverId,
                RefreshProtected = refreshProtected,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.RefreshProtected = refreshProtected;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async ValueTask<bool> HasRefreshAsync(Guid userId, string serverId, CancellationToken ct)
    {
        return await db.McpOAuthCredentials.AsNoTracking()
            .AnyAsync(credential => credential.UserId == userId && credential.ServerId == serverId, ct);
    }

    public async ValueTask<McpOAuthCredential?> FindSingleAsync(string serverId, CancellationToken ct)
    {
        var rows = await db.McpOAuthCredentials.AsNoTracking()
            .Where(credential => credential.ServerId == serverId)
            .Take(2)
            .ToListAsync(ct);
        if (rows.Count != 1)
        {
            return null;
        }

        var row = rows[0];
        return new McpOAuthCredential(row.UserId, row.ServerId, row.RefreshProtected, row.UpdatedAt);
    }
}
