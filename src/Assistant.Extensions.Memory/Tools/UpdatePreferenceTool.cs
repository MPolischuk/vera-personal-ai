using System.Diagnostics;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Events;
using Assistant.Core.Memory;
using Assistant.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Tools;

internal sealed class UpdatePreferenceTool(
    IPreferenceStore store,
    IEventBus events,
    ILogger<UpdatePreferenceTool> logger) : ITool
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "key": {
                  "type": "string",
                  "description": "Clave estable en snake_case ASCII, por ejemplo bedroom_temp."
                },
                "value": {
                  "type": "string",
                  "description": "Valor de la preferencia. No puede estar vacío."
                }
              },
              "required": ["key", "value"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "update_preference",
        "Usala cuando el usuario fije un hábito o setting reutilizable con clave estable en snake_case ASCII "
        + "(por ejemplo bedroom_temp = 23). Llamala en ese turno; no alcanza con decir que quedó. "
        + "No la uses para prosa biográfica (eso es remember), gustos de Vera, ni para recitar el almacén.",
        Schema,
        PermissionLevel.ReversibleAction,
        "memory",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("key", out var rawKey))
        {
            return ToolResult.Failed("Falta el parámetro obligatorio 'key'.");
        }

        var key = rawKey.Trim();
        if (key.Length == 0)
        {
            return ToolResult.Failed("El parámetro 'key' no puede estar vacío.");
        }

        if (!PreferenceKeys.IsValid(key))
        {
            return ToolResult.Failed(
                "La clave debe ser snake_case ASCII: minúsculas, dígitos y guion bajo, empezando por una letra.");
        }

        if (!args.TryGetString("value", out var rawValue))
        {
            return ToolResult.Failed("Falta el parámetro obligatorio 'value'.");
        }

        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            return ToolResult.Failed("El parámetro 'value' no puede estar vacío.");
        }

        using var activity = MemoryTelemetry.ActivitySource.StartActivity(GenAiSpans.UpsertMemory);
        var item = await store.UpsertAsync(
            context.UserId, key, value, WriteProvenance.FromModelTurn(context.TurnContaminated), ct);
        await events.PublishAsync(
            new PreferenceUpdated(context.UserId, key, context.ConversationId, context.TurnId),
            ct);

        activity?.SetTag(GenAiSpans.MemoryStoreId, GenAiSpans.StorePreferences);
        activity?.SetTag(GenAiSpans.MemoryRecordId, item.Id.ToString());
        MemoryTelemetry.PreferenceUpserts.Add(1);
        MemoryLog.PreferenceUpserted(logger, key);
        return ToolResult.Ok(MemoryToolJson.Preference(item));
    }
}
