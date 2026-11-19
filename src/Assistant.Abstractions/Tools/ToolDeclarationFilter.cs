namespace Assistant.Abstractions.Tools;

/// <summary>
/// A contributor can hide tools for this turn after it already answered
/// what those tools would have fetched. Scoped: one instance per turn scope.
/// </summary>
public sealed class ToolDeclarationFilter
{
    private readonly List<string> _names = [];
    private readonly List<string> _prefixes = [];

    public bool IsActive => _names.Count > 0 || _prefixes.Count > 0;

    public void ExcludeName(string name) => _names.Add(name);

    public void ExcludePrefix(string prefix) => _prefixes.Add(prefix);

    public IReadOnlyList<ToolDescriptor> Apply(IReadOnlyList<ToolDescriptor> tools)
    {
        if (!IsActive)
        {
            return tools;
        }

        return tools
            .Where(tool => !Excluded(tool.Name))
            .ToList();
    }

    private bool Excluded(string name)
        => _names.Contains(name, StringComparer.Ordinal)
           || _prefixes.Exists(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
