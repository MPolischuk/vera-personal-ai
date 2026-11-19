using System.Text.Json;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Tasks;
using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Delegation;

/// <summary>
/// Inserts a draft and returns its id. The model call runs later, on the worker.
/// </summary>
internal sealed class DelegateWorkTool(
    IAgentTaskStore store,
    INotificationPublisher publisher,
    IOptionsMonitor<DelegationOptions> options,
    TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "instruction"],
          "properties": {
            "title": {
              "type": "string",
              "description": "Título corto de la tarea."
            },
            "instruction": {
              "type": "string",
              "description": "Qué tiene que redactar el borrador."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "delegate_work",
        "Deja un borrador en segundo plano y devuelve el id enseguida. "
        + "El resultado llega después, dicho por Vera.",
        Schema,
        PermissionLevel.ReversibleAction,
        "delegation",
        OutputTrust.Trusted,
        WritesDurableState: false);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        if (!args.TryGetString("title", out var title))
        {
            return ToolResult.Failed($"Pasame un título de hasta {settings.TitleMaxCharacters} caracteres.");
        }

        if (!args.TryGetString("instruction", out var instruction))
        {
            return ToolResult.Failed(
                $"Pasame una instrucción de hasta {settings.InstructionMaxCharacters} caracteres.");
        }

        title = title.Trim();
        instruction = instruction.Trim();
        if (title.Length is 0 || title.Length > settings.TitleMaxCharacters)
        {
            return ToolResult.Failed($"El título tiene que tener entre 1 y {settings.TitleMaxCharacters} caracteres.");
        }

        if (instruction.Length is 0 || instruction.Length > settings.InstructionMaxCharacters)
        {
            return ToolResult.Failed(
                $"La instrucción tiene que tener entre 1 y {settings.InstructionMaxCharacters} caracteres.");
        }

        var now = clock.GetUtcNow();
        var state = settings.Enabled ? AgentTaskStatuses.Submitted : AgentTaskStatuses.Rejected;
        var row = await store.InsertAsync(
            new AgentTaskInsert(
                context.UserId,
                context.ConversationId,
                context.TurnId,
                Key(context.ConversationId, context.ToolCallId),
                DraftWorker.WorkerKind,
                title,
                instruction,
                settings.CostCapUsd,
                now.Add(settings.TimeCap),
                Idempotent: false,
                context.TurnId,
                state),
            now,
            ct);

        if (row.State == AgentTaskStatuses.Submitted)
        {
            DraftHandoff.RememberTurn(row.Id, context.TurnId);
        }

        if (row.State == AgentTaskStatuses.Rejected)
        {
            await AgentTaskNoticePublisher.PublishArmedAsync(store, publisher, clock.GetUtcNow(), ct);
        }

        var text = row.State == AgentTaskStatuses.Submitted
            ? $"Quedó la tarea {row.Id:D} en curso."
            : $"Quedó la tarea {row.Id:D} en {row.State}.";
        return ToolResult.Ok(text);
    }

    internal static string Key(Guid conversationId, string? toolCallId)
    {
        var call = string.IsNullOrWhiteSpace(toolCallId)
            ? Guid.NewGuid().ToString("N")
            : toolCallId.Trim();
        var key = $"delegate:{conversationId:D}:{call}";
        return key.Length <= 200 ? key : key[..200];
    }
}
