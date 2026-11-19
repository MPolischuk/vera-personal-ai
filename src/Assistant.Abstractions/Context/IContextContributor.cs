namespace Assistant.Abstractions.Context;

/// <summary>
/// Seam 1: new information enters the prompt here. Registering an implementation
/// must not require changing any existing core file (criterion B1).
/// </summary>
public interface IContextContributor
{
    public string Name { get; }

    /// <summary>
    /// Lower is more important. <see cref="ContextPriority.Mandatory"/> is never dropped:
    /// if it does not fit the budget, the turn fails.
    /// </summary>
    public int Priority { get; }

    public bool IsEnabled { get; }

    public ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct);
}

public static class ContextPriority
{
    public const int Mandatory = 0;
}
