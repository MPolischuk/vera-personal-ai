using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Proactive.Tools;

internal sealed class SetConditionalIntentTool(IConditionalIntentStore intents, TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["event_name", "text"],
          "properties": {
            "event_name": {
              "type": "string",
              "enum": ["{{ConditionalIntentEvents.InboundFact}}"],
              "description": "El único evento que dispara es inbound.fact."
            },
            "text": {
              "type": "string",
              "minLength": 1,
              "maxLength": {{ConditionalIntentLimits.TextMaxLength}},
              "description": "El texto del aviso. No incluye la transcripción del anillo."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "set_conditional_intent",
        "Guarda un aviso para cuando llegue inbound.fact. Otro evento se rechaza.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("event_name", out var eventName) || !ConditionalIntentEvents.IsAllowed(eventName))
        {
            return ToolResult.Failed("Ese evento no está en la lista. Sólo se acepta inbound.fact.");
        }

        if (!args.TryGetString("text", out var text))
        {
            return ToolResult.Failed("Falta el texto de la intención.");
        }

        try
        {
            var intent = await intents.InsertAsync(context.UserId, eventName, text, clock.GetUtcNow(), ct);
            return ToolResult.Ok(intent.Id.ToString("D"));
        }
        catch (ArgumentException)
        {
            return ToolResult.Failed("La intención tiene que tener entre 1 y 300 caracteres.");
        }
    }
}

internal sealed class PauseConditionalIntentTool(IConditionalIntentStore intents) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["intent_id"],
          "properties": {
            "intent_id": { "type": "string", "description": "Id de una intención activa de esta persona." }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "pause_conditional_intent",
        "Pausa una intención activa.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("intent_id", out var raw) || !Guid.TryParse(raw, out var intentId))
        {
            return ToolResult.Failed("Falta el id de la intención.");
        }

        var paused = await intents.PauseAsync(context.UserId, intentId, ct);
        return paused ? ToolResult.Ok("ok") : ToolResult.Failed("No encontré esa intención activa.");
    }
}

internal sealed class ListConditionalIntentsTool(IConditionalIntentStore intents) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {} }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_conditional_intents",
        "Lista las intenciones, activas y pausadas.",
        Schema,
        PermissionLevel.Read,
        "proactive");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var rows = await intents.ListAsync(context.UserId, ct);
        if (rows.Count == 0)
        {
            return ToolResult.Ok("[]");
        }

        var lines = rows.Select(intent => FormattableString.Invariant(
            $"{intent.Id:D} {intent.EventName} {intent.Status} {OneLine(intent.Text)}"));
        return ToolResult.Ok(string.Join('\n', lines));
    }

    private static string OneLine(string text)
        => string.Join(' ', text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
}
