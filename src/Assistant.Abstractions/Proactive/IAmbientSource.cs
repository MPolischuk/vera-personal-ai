namespace Assistant.Abstractions.Proactive;

/// <summary>
/// Read-only context for a briefing or a heartbeat. It is not an
/// <c>IContextContributor</c>: the turn assembler never sees it.
/// </summary>
public interface IAmbientSource
{
    /// <summary>Stable name used as a prompt heading. For example, <c>calendar</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Text for the prompt, or null when the source has nothing to add.
    /// A missing residence or a disconnected calendar returns null and the brief continues.
    /// </summary>
    public ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct);
}
