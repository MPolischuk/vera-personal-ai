using System.Diagnostics;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Events;
using Assistant.Core.Memory;
using Assistant.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Tools;

internal sealed class ForgetTool(
    IMemoryStore store,
    IEventBus events,
    ILogger<ForgetTool> logger) : ITool, IToolConfirmationDetail
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "memory_id": {
                  "type": "string",
                  "format": "uuid",
                  "description": "Id del recuerdo a olvidar, tal como lo devolvió recall o search_memory."
                }
              },
              "required": ["memory_id"],
              "additionalProperties": false
            }
            """)
        .RootElement.Clone();

    public ToolDescriptor Descriptor { get; } = new(
        "forget",
        "Olvida un recuerdo por su memory_id. Requiere confirmación del usuario. "
        + "No inventes el id: usá uno devuelto por recall o search_memory.",
        Schema,
        PermissionLevel.SensitiveAction,
        "memory",
        WritesDurableState: true);

    public async ValueTask<ToolConfirmationText> DescribeAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var target = await FindTargetAsync(args, context, ct);
        return target.Error is null
            ? ToolConfirmationText.Show(target.Item!.Content)
            : ToolConfirmationText.Failed(target.Error);
    }

    public async ValueTask<ToolResult> ExecuteAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var target = await FindTargetAsync(args, context, ct);
        if (target.Error is not null)
        {
            return ToolResult.Failed(target.Error);
        }

        var memoryId = target.Id;
        using var activity = MemoryTelemetry.ActivitySource.StartActivity(GenAiSpans.DeleteMemory);
        await store.ForgetAsync(memoryId, context.UserId, ct);
        await events.PublishAsync(
            new MemoryForgotten(context.UserId, memoryId, context.ConversationId, context.TurnId),
            ct);

        activity?.SetTag(GenAiSpans.MemoryStoreId, GenAiSpans.StoreMemories);
        activity?.SetTag(GenAiSpans.MemoryRecordId, memoryId.ToString());
        MemoryTelemetry.Forgets.Add(1);
        MemoryLog.Forgotten(logger, memoryId);
        return ToolResult.Ok(MemoryToolJson.Forgotten(memoryId));
    }

    private async ValueTask<MemoryTarget> FindTargetAsync(
        ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        if (!args.TryGetString("memory_id", out var rawId))
        {
            return MemoryTarget.Fail("Falta el parámetro obligatorio 'memory_id'.");
        }

        if (!Guid.TryParse(rawId, out var memoryId))
        {
            return MemoryTarget.Fail("El parámetro 'memory_id' no es un uuid válido.");
        }

        var existing = await store.FindAsync(memoryId, context.UserId, ct);
        return existing is null
            ? MemoryTarget.Fail("No existe un recuerdo con ese id.")
            : MemoryTarget.Found(memoryId, existing);
    }

    private readonly record struct MemoryTarget(Guid Id, MemoryItem? Item, string? Error)
    {
        public static MemoryTarget Fail(string error) => new(Guid.Empty, null, error);

        public static MemoryTarget Found(Guid id, MemoryItem item) => new(id, item, null);
    }
}
