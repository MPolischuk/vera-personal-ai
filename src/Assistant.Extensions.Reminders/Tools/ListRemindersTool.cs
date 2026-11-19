using System.Text.Json;
using Assistant.Abstractions.Reminders;
using Assistant.Abstractions.Tools;
using Assistant.Extensions.Reminders.Time;

namespace Assistant.Extensions.Reminders.Tools;

internal sealed class ListRemindersTool(IReminderQueryService query) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "status": {
              "type": "string",
              "enum": ["active", "missed", "all"],
              "description": "Cuáles listar. Si no lo pasás, son los activos."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 50,
              "description": "Máximo de filas. El default es 20."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_reminders",
        "Lista los recordatorios del usuario con la próxima hora en su zona. "
        + "Cada activo trae short_ref: 1 es el más próximo. Usá ese número para cancelar («el 2»).",
        Schema,
        PermissionLevel.Read,
        "reminders");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!ReminderArgumentReader.TryReadList(args, out var status, out var limit, out var error))
        {
            return ToolResult.Failed(error);
        }

        var scope = status switch
        {
            "missed" => ReminderListScope.Missed,
            "all" => ReminderListScope.All,
            _ => ReminderListScope.Active,
        };
        var items = await query.ListAsync(context.UserId, scope, limit ?? 20, ct);
        var listed = new List<ReminderToolJson.ListedItem>(items.Count);
        foreach (var item in items)
        {
            listed.Add(new ReminderToolJson.ListedItem(
                item.Id,
                item.ShortRef,
                item.Text,
                item.Status,
                LocalDue(item),
                Describe(item)));
        }

        return ToolResult.Ok(ReminderToolJson.Listed(listed));
    }

    private static string? LocalDue(ReminderListItem item)
    {
        if (item.NextOccurrenceAt is not DateTimeOffset next)
        {
            return null;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZone);
        return ReminderTimeText.LocalDue(ReminderTimeText.NowLocal(next, zone));
    }

    private static string? Describe(ReminderListItem item)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(item.TimeZone, out var zone))
        {
            return null;
        }

        return RecurrenceSpec.TryParseRule(item.RecurrenceRule, zone, out var spec) && spec is not null
            ? spec.Describe(item.LocalStart)
            : null;
    }
}
