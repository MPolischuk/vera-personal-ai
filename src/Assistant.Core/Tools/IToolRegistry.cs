using System.Diagnostics.CodeAnalysis;
using Assistant.Abstractions.Tools;

namespace Assistant.Core.Tools;

public interface IToolRegistry
{
    public IReadOnlyList<ToolDescriptor> GetAvailableTools(ToolExecutionContext context);

    public bool TryGetDescriptor(string name, [NotNullWhen(true)] out ToolDescriptor? descriptor);
}
