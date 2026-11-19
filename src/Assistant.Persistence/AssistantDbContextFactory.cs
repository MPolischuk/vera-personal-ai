using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Assistant.Persistence;

public sealed class AssistantDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    // No password on purpose: enough to generate migrations and fails clearly when applying
    // if ASSISTANT_DB_CONNECTION was never set. No secrets in the repository (C5).
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=assistant;Username=assistant";

    public AssistantDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ASSISTANT_DB_CONNECTION") ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<AssistantDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureContext(options, connection);

        return new AssistantDbContext(options.Options);
    }
}
