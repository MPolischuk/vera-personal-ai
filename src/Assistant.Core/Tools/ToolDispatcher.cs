using System.Diagnostics;
using System.Text.Json;
using Assistant.Abstractions.Tools;
using Assistant.Core.Configuration;
using Assistant.Core.Conversations;
using Assistant.Core.Events;
using Assistant.Core.Observability;
using Assistant.Core.Permissions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Tools;

internal sealed class ToolDispatcher(
    ToolCatalog catalog,
    IPermissionEngine permissions,
    IEventBus events,
    IOptions<ToolOptions> options,
    TimeProvider clock,
    ILogger<ToolDispatcher> log) : IToolDispatcher
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("{}").RootElement.Clone();

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        string name, ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        var start = clock.GetTimestamp();

        if (!catalog.TryResolve(name, out var tool))
        {
            // The model hallucinated a name. Error result, not an exception:
            // the turn continues and the model can correct itself.
            return await ResolveAsync(
                context, UnknownDescriptor(name), args,
                PermissionDecision.Deny($"No existe una tool llamada '{name}'."),
                result: null, start, ct);
        }

        var decision = await permissions.EvaluateAsync(tool.Descriptor, args, context, ct);

        if (decision.Outcome is PermissionOutcome.RequireConfirmation
            && tool is IToolConfirmationDetail detail)
        {
            var described = await detail.DescribeAsync(args, context, ct);
            if (described.Error is not null)
            {
                return await ResolveAsync(
                    context,
                    tool.Descriptor,
                    args,
                    PermissionDecision.Allow(),
                    ToolResult.Failed(described.Error),
                    start,
                    ct);
            }

            if (described.Prompt is not null)
            {
                decision = decision with { ConfirmationPrompt = described.Prompt };
            }
        }

        if (decision.Outcome is not PermissionOutcome.Allow)
        {
            return await ResolveAsync(context, tool.Descriptor, args, decision, result: null, start, ct);
        }

        var result = await ExecuteAsync(tool, args, context, ct);

        return await ResolveAsync(context, tool.Descriptor, args, decision, result, start, ct);
    }

    private async ValueTask<ToolResult> ExecuteAsync(
        ITool tool, ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

        try
        {
            return await tool.ExecuteAsync(args, context, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout, not user interrupt. Treated as failure.
            // Spanish: product copy returned to the model/user.
            return ToolResult.Failed($"La tool '{tool.Descriptor.Name}' excedió el tiempo de espera.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Design §9: return the error to the model so it can react, and audit it.
            CoreLog.ToolFailed(log, e, tool.Descriptor.Name);
            return ToolResult.Failed(e.Message);
        }
    }

    // Single exit point: measure, publish ToolInvoked, build the return value.
    // Being one place is what makes "every invocation is audited" not depend on memory.
    private async ValueTask<ToolInvocationResult> ResolveAsync(
        ToolExecutionContext context,
        ToolDescriptor descriptor,
        ToolArguments args,
        PermissionDecision decision,
        ToolResult? result,
        long start,
        CancellationToken ct)
    {
        using var activity = TurnTelemetry.ActivitySource.StartActivity(
            $"{GenAiSpans.ExecuteTool} {descriptor.Name}",
            ActivityKind.Internal);
        activity?.SetTag(GenAiSpans.ToolName, descriptor.Name);
        if (!string.IsNullOrWhiteSpace(context.ToolCallId))
        {
            activity?.SetTag(GenAiSpans.ToolCallId, context.ToolCallId);
        }

        activity?.SetTag(GenAiSpans.PermissionOutcome, decision.Outcome.ToString());

        // One string for the model, the audit event, and ToolInvocationRecord.
        // A result may override the descriptor. Null keeps the descriptor trust.
        if (result is not null
            && (result.OutputTrust ?? descriptor.OutputTrust) is OutputTrust.Untrusted)
        {
            result = UntrustedContentEnvelope.Apply(descriptor, result);
        }

        var duration = clock.GetElapsedTime(start);

        CoreTelemetry.Invocations.Add(
            1,
            new KeyValuePair<string, object?>("tool", descriptor.Name),
            new KeyValuePair<string, object?>("outcome", decision.Outcome.ToString()));

        await events.PublishAsync(
            new ToolInvoked(
                context,
                descriptor.Name,
                descriptor.ExtensionName,
                descriptor.Level,
                decision.Outcome,
                decision.Reason,
                args.RawJson,
                result,
                duration),
            ct);

        return new ToolInvocationResult(
            decision.Outcome,
            result,
            decision.Reason,
            decision.ConfirmationPrompt,
            duration);
    }

    private static ToolDescriptor UnknownDescriptor(string name)
        => new(
            name,
            string.Empty,
            EmptySchema,
            PermissionLevel.Undeclared,
            "unknown",
            OutputTrust.Untrusted);
}
