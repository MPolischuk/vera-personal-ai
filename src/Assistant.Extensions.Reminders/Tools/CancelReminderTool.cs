using System.Text.Json;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Tools;
using Assistant.Extensions.Reminders.Time;

namespace Assistant.Extensions.Reminders.Tools;

internal sealed class CancelReminderTool(IReminderStore store, IReminderQueryService query) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "id": {
              "type": "string",
              "format": "uuid",
              "description": "Id del recordatorio."
            },
            "short_ref": {
              "type": "integer",
              "minimum": 1,
              "description": "Número que devolvió list_reminders. 1 es el más próximo."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "cancel_reminder",
        "Cancela un recordatorio activo completo, incluidas las próximas ocurrencias. "
        + "Pasá id o short_ref, uno solo. short_ref es el número de list_reminders.",
        Schema,
        PermissionLevel.ReversibleAction,
        "reminders",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!ReminderArgumentReader.TryReadCancel(args, out var id, out var shortRef, out var error))
        {
            return ToolResult.Failed(error);
        }

        var reminderId = id;
        if (shortRef is int position)
        {
            reminderId = await query.FindActiveIdByShortRefAsync(context.UserId, position, ct);
            if (reminderId is null)
            {
                return ToolResult.Failed(ReminderMessages.ShortRefMissing);
            }
        }

        var cancelled = await store.CancelAsync(context.UserId, reminderId!.Value, ct);
        return cancelled switch
        {
            CancelReminderResult.Cancelled done => ToolResult.Ok(
                ReminderToolJson.Cancelled(done.Reminder.Id, done.Reminder.Text)),
            CancelReminderResult.Rejected { Reason: ReminderRejection.NotActive }
                => ToolResult.Failed(ReminderMessages.NotActive),
            _ => ToolResult.Failed(ReminderMessages.NotFound),
        };
    }
}
