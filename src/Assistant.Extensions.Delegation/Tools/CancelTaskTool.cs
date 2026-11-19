using System.Text.Json;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Delegation.Tools;

internal sealed class CancelTaskTool(
    IAgentTaskStore store,
    INotificationPublisher publisher,
    AgentTaskRuns runs,
    TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["id"],
          "properties": {
            "id": {
              "type": "string",
              "format": "uuid",
              "description": "Id de la tarea."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "cancel_task",
        "Cancela una tarea enviada, en curso o esperando confirmación. No espera a que el proceso termine.",
        Schema,
        PermissionLevel.ReversibleAction,
        "delegation",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("id", out var raw) || !Guid.TryParse(raw, out var id))
        {
            return ToolResult.Failed(DelegationMessages.CancelIdRequired);
        }

        var cancelled = await AgentTaskCancellation.TryCancelAsync(
            store, publisher, runs, clock, id, context.UserId, ct);
        if (!cancelled)
        {
            return ToolResult.Failed(DelegationMessages.CancelRejected);
        }

        return ToolResult.Ok(DelegationToolJson.Cancelled(id));
    }
}
