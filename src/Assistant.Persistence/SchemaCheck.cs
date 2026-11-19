using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Persistence;

public static class SchemaCheck
{
    public static async Task EnsureUpToDateAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
        var pending = await context.Database.GetPendingMigrationsAsync(ct);
        var migrations = pending.ToArray();

        if (migrations.Length != 0)
        {
            throw new InvalidOperationException(
                "The Assistant database schema is not up to date. Pending migrations: "
                + string.Join(", ", migrations)
                + ".");
        }
    }
}
