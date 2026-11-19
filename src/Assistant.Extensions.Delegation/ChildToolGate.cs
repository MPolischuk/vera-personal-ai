using System.Text.Json;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;
using Assistant.Abstractions.Tools;
using Assistant.Core.Permissions;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Delegation;

/// <summary>
/// Decides whether a child tool may run. A held or denied tool is not executed.
/// </summary>
internal sealed class ChildToolGate(
    IPermissionEngine permissions,
    IOptionsMonitor<DelegationOptions> options,
    TimeProvider clock)
{
    public async ValueTask<ChildToolVerdict> AdmitAsync(
        IAgentTaskStore store,
        string workerId,
        Guid taskId,
        Guid userId,
        ToolDescriptor tool,
        ToolArguments args,
        ToolExecutionContext context,
        Func<CancellationToken, ValueTask> execute,
        INotificationPublisher publisher,
        CancellationToken ct)
    {
        if (LeavesNetwork(args, options.CurrentValue.EgressHosts))
        {
            await HoldAsync(
                store,
                publisher,
                workerId,
                taskId,
                userId,
                context.ConversationId,
                tool.Name,
                "Un tool quiere salir a una dirección que no está permitida.",
                ct);
            return new ChildToolVerdict(false, AgentTaskStatuses.InputRequired);
        }

        var decision = await permissions.EvaluateAsync(tool, args, context, ct);
        if (decision.Outcome == PermissionOutcome.Allow)
        {
            await execute(ct);
            return new ChildToolVerdict(true, null);
        }

        if (decision.Outcome == PermissionOutcome.RequireConfirmation)
        {
            var prompt = decision.ConfirmationPrompt
                ?? $"¿Autorizás que ejecute «{tool.Name}»?";
            await HoldAsync(
                store,
                publisher,
                workerId,
                taskId,
                userId,
                context.ConversationId,
                tool.Name,
                prompt,
                ct);
            return new ChildToolVerdict(false, AgentTaskStatuses.InputRequired);
        }

        var wrote = await store.CompleteAsync(
            taskId,
            workerId,
            new AgentTaskClose(AgentTaskStatuses.Rejected, null),
            clock.GetUtcNow(),
            ct);
        if (wrote)
        {
            await store.DisarmNoticeAsync(taskId, clock.GetUtcNow(), ct);
        }

        return new ChildToolVerdict(false, AgentTaskStatuses.Rejected);
    }

    private async Task HoldAsync(
        IAgentTaskStore store,
        INotificationPublisher publisher,
        string workerId,
        Guid taskId,
        Guid userId,
        Guid conversationId,
        string toolName,
        string body,
        CancellationToken ct)
    {
        var wrote = await store.CompleteAsync(
            taskId,
            workerId,
            new AgentTaskClose(AgentTaskStatuses.InputRequired, null),
            clock.GetUtcNow(),
            ct);
        if (!wrote)
        {
            return;
        }

        var title = toolName.Length <= 60 ? toolName : toolName[..60];
        var text = body.Length <= 1000 ? body : body[..1000];
        await publisher.PublishAsync(
            new NotificationRequest(
                userId,
                NotificationKind.ConfirmationRequest,
                $"task:{taskId:D}:confirm:{toolName}",
                title,
                text,
                ConversationId: conversationId,
                SourceType: "task",
                SourceId: taskId.ToString("D"),
                Trust: NotificationContentTrust.Untrusted),
            ct);
    }

    private static bool LeavesNetwork(ToolArguments args, IReadOnlyCollection<string> hosts)
    {
        try
        {
            using var document = JsonDocument.Parse(args.RawJson);
            return ContainsOutsideUrl(document.RootElement, hosts);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsOutsideUrl(JsonElement element, IReadOnlyCollection<string> hosts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return IsOutside(element.GetString(), hosts);
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsOutsideUrl(property.Value, hosts))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsOutsideUrl(item, hosts))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool IsOutside(string? text, IReadOnlyCollection<string> hosts)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        foreach (var host in hosts)
        {
            if (string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

internal readonly record struct ChildToolVerdict(bool MayExecute, string? State);
