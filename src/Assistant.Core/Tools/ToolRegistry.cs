using System.Diagnostics.CodeAnalysis;
using Assistant.Abstractions.Tools;

namespace Assistant.Core.Tools;

internal sealed class ToolRegistry(ToolCatalog catalog) : IToolRegistry
{
    // Stage 1a does not filter. Context is in the signature so V8 can filter by user
    // or device without changing the port.
    public IReadOnlyList<ToolDescriptor> GetAvailableTools(ToolExecutionContext context)
        => catalog.Descriptors;

    public bool TryGetDescriptor(string name, [NotNullWhen(true)] out ToolDescriptor? descriptor)
        => catalog.TryGetDescriptor(name, out descriptor);
}
