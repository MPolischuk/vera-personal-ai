namespace Assistant.Abstractions.Tools;

/// <summary>
/// Dynamic source of tools discovered at runtime (e.g. MCP). Merged into the
/// catalog after in-process <see cref="ITool"/> registrations.
/// </summary>
public interface IToolSource
{
    public IReadOnlyList<ITool> Tools { get; }
}
