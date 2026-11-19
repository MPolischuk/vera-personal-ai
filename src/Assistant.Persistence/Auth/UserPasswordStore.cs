using Assistant.Core;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Auth;

internal sealed class UserPasswordStore(AssistantDbContext db) : IUserPasswordStore
{
    public async Task<string?> GetHashAsync(CancellationToken cancellationToken)
    {
        var user = await RequireDefaultUserAsync(cancellationToken);
        return user.PasswordHash;
    }

    public async Task SetHashAsync(string passwordHash, CancellationToken cancellationToken)
    {
        var user = await RequireDefaultUserAsync(cancellationToken);
        user.PasswordHash = passwordHash;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<UserEntity> RequireDefaultUserAsync(CancellationToken cancellationToken)
        => await db.Users.SingleOrDefaultAsync(u => u.Id == WellKnownIds.DefaultUser, cancellationToken)
            ?? throw new InvalidOperationException("The default user row is missing.");
}
