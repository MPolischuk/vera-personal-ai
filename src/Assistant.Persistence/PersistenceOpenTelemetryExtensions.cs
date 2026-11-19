using Npgsql;
using OpenTelemetry.Trace;

namespace Assistant.Persistence;

public static class PersistenceOpenTelemetryExtensions
{
    public static TracerProviderBuilder AddAssistantPersistenceInstrumentation(
        this TracerProviderBuilder builder)
        => builder.AddNpgsql();
}
