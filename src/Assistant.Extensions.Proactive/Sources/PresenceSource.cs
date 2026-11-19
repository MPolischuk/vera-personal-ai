using Assistant.Abstractions.Proactive;

namespace Assistant.Extensions.Proactive.Sources;

/// <summary>Fixed presence until a real source exists. It does not call Home Assistant.</summary>
public sealed class PresenceSource : IAmbientSource
{
    public const string Unknown = "desconocida";

    public string Name => "presence";

    public ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
        => ValueTask.FromResult<string?>(Unknown);
}
