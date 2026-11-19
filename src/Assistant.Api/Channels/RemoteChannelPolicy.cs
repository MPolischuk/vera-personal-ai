using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Tools;
using Assistant.Core.Permissions;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Channels;

/// <summary>
/// Denies tools that must be confirmed on the web when the turn entered from a remote channel.
/// With the default <c>Channels:Remote</c> section the inner decision is unchanged.
/// </summary>
internal sealed class RemoteChannelPolicy(
    IPermissionPolicy inner,
    ITurnOriginAccessor origin,
    IOptions<ChannelsOptions> options) : IPermissionPolicy
{
    public async ValueTask<PermissionDecision> EvaluateAsync(
        ToolDescriptor tool, ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var decision = await inner.EvaluateAsync(tool, args, context, ct);
        if (origin.Current.Channel == ChannelId.Hub)
        {
            return decision;
        }

        var rules = options.Value.Remote;
        var webOnly = rules.WebOnlyTools ?? [];
        if (webOnly.Contains(tool.Name, StringComparer.Ordinal)
            || (!rules.AllowSensitiveActions && tool.Level is PermissionLevel.SensitiveAction))
        {
            return PermissionDecision.Deny("Esta acción sólo se confirma desde la app.");
        }

        return decision;
    }
}
