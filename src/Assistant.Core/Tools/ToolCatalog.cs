using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Tools;

internal sealed class ToolCatalog
{
    private static readonly Regex IsValidName = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly Dictionary<string, ITool> _byName;

    public ToolCatalog(
        IEnumerable<ITool> tools,
        IEnumerable<IToolSource>? sources = null,
        ILogger<ToolCatalog>? logger = null)
    {
        _byName = new Dictionary<string, ITool>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            var descriptor = tool.Descriptor;

            // Conventions require snake_case because the name is exposed to the LLM.
            // An invalid name fails when building the catalog, not mid-turn.
            if (!IsValidName.IsMatch(descriptor.Name))
            {
                throw new InvalidOperationException(
                    $"Tool '{descriptor.Name}' from {descriptor.ExtensionName} does not use snake_case.");
            }

            if (!_byName.TryAdd(descriptor.Name, tool))
            {
                throw new InvalidOperationException(
                    $"Two tools declare the name '{descriptor.Name}': " +
                    $"{_byName[descriptor.Name].Descriptor.ExtensionName} and {descriptor.ExtensionName}. " +
                    "The catalog the LLM sees does not allow duplicate names.");
            }
        }

        if (sources is not null)
        {
            foreach (var source in sources)
            {
                foreach (var tool in source.Tools)
                {
                    var descriptor = tool.Descriptor;

                    if (!IsValidName.IsMatch(descriptor.Name))
                    {
                        if (logger is not null)
                        {
                            ToolCatalogLog.OmittingInvalidName(
                                logger, descriptor.Name, descriptor.ExtensionName);
                        }

                        continue;
                    }

                    if (!_byName.TryAdd(descriptor.Name, tool))
                    {
                        if (logger is not null)
                        {
                            ToolCatalogLog.OmittingDuplicateName(
                                logger,
                                descriptor.Name,
                                descriptor.ExtensionName,
                                _byName[descriptor.Name].Descriptor.ExtensionName);
                        }

                        continue;
                    }
                }
            }
        }

        foreach (var tool in _byName.Values)
        {
            if (tool is IBindsToolNames binder)
            {
                binder.Bind(() => Names);
            }
        }
    }

    public IReadOnlyCollection<string> Names => _byName.Keys;

    public IReadOnlyList<ToolDescriptor> Descriptors =>
        _byName.Values
            .Select(t => t.Descriptor)
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

    public bool TryResolve(string name, [NotNullWhen(true)] out ITool? tool)
        => _byName.TryGetValue(name, out tool);

    public bool TryGetDescriptor(string name, [NotNullWhen(true)] out ToolDescriptor? descriptor)
    {
        if (_byName.TryGetValue(name, out var tool))
        {
            descriptor = tool.Descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }
}
