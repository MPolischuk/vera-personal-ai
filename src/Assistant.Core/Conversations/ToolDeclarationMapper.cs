using Assistant.Abstractions.Tools;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Conversations;

internal static class ToolDeclarationMapper
{
    /// <summary>
    /// Declarations, not functions: the model may request them and MEAI cannot execute them.
    /// That is ADR-0012 as a type, and a test locks it in (D3).
    /// </summary>
    public static IList<AITool> Declare(IReadOnlyList<ToolDescriptor> descriptors)
        => [.. descriptors.Select(d =>
            (AITool)AIFunctionFactory.CreateDeclaration(d.Name, d.Description, d.ParametersSchema))];
}
