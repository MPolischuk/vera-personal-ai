using Assistant.Abstractions.Tools;
using Assistant.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Permissions;

internal sealed class DefaultPermissionPolicy(IOptions<PermissionOptions> options) : IPermissionPolicy
{
    public ValueTask<PermissionDecision> EvaluateAsync(
        ToolDescriptor tool, ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var decision = tool.Level switch
        {
            PermissionLevel.Read => PermissionDecision.Allow(),
            PermissionLevel.ReversibleAction => options.Value.AutoAllowReversible
                ? PermissionDecision.Allow()
                : PermissionDecision.Confirm(
                    $"El asistente quiere ejecutar «{tool.Name}»: {tool.Description}. ¿Lo autorizás?"),
            PermissionLevel.SensitiveAction => PermissionDecision.Confirm(
                $"El asistente quiere ejecutar «{tool.Name}»: {tool.Description}. ¿Lo autorizás?"),
            _ => PermissionDecision.Deny("La tool no declara su nivel de permiso."),
        };

        return ValueTask.FromResult(decision);
    }
}
