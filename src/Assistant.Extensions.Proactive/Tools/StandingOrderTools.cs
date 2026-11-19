using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tools;

namespace Assistant.Extensions.Proactive.Tools;

internal sealed class SetStandingOrderTool(IStandingOrderStore orders, TimeProvider clock) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["text"],
          "properties": {
            "text": {
              "type": "string",
              "minLength": 1,
              "maxLength": {{StandingOrderLimits.TextMaxLength}},
              "description": "La orden permanente, dicha por la persona. No la infieras del chat."
            }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "set_standing_order",
        "Guarda una orden que vale siempre. Entra al heartbeat y no al turno normal.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("text", out var text))
        {
            return ToolResult.Failed("Falta el texto de la orden.");
        }

        try
        {
            var order = await orders.InsertAsync(context.UserId, text, clock.GetUtcNow(), ct);
            return ToolResult.Ok(order.Id.ToString("D"));
        }
        catch (ArgumentException)
        {
            return ToolResult.Failed("La orden tiene que tener entre 1 y 500 caracteres.");
        }
    }
}

internal sealed class PauseStandingOrderTool(IStandingOrderStore orders) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["order_id"],
          "properties": {
            "order_id": { "type": "string", "description": "Id de una orden permanente activa de esta persona." }
          }
        }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "pause_standing_order",
        "Pausa una orden permanente activa.",
        Schema,
        PermissionLevel.ReversibleAction,
        "proactive",
        WritesDurableState: true);

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("order_id", out var raw) || !Guid.TryParse(raw, out var orderId))
        {
            return ToolResult.Failed("Falta el id de la orden.");
        }

        var paused = await orders.PauseAsync(context.UserId, orderId, ct);
        return paused ? ToolResult.Ok("ok") : ToolResult.Failed("No encontré esa orden activa.");
    }
}

internal sealed class ListStandingOrdersTool(IStandingOrderStore orders) : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        { "type": "object", "additionalProperties": false, "properties": {} }
        """).RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "list_standing_orders",
        "Lista las órdenes permanentes, activas y pausadas.",
        Schema,
        PermissionLevel.Read,
        "proactive");

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var rows = await orders.ListAsync(context.UserId, ct);
        if (rows.Count == 0)
        {
            return ToolResult.Ok("[]");
        }

        var lines = rows.Select(order => FormattableString.Invariant(
            $"{order.Id:D} {order.Status} {OneLine(order.Text)}"));
        return ToolResult.Ok(string.Join('\n', lines));
    }

    private static string OneLine(string text)
        => string.Join(' ', text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
}
