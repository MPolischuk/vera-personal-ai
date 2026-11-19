using Assistant.Abstractions.Tools;

namespace Assistant.Core.Permissions;

public interface IPermissionPolicy
{
    public ValueTask<PermissionDecision> EvaluateAsync(
        ToolDescriptor tool, ToolArguments args, ToolExecutionContext context, CancellationToken ct);
}
