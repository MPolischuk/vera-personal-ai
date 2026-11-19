using Assistant.Abstractions.Tools;
using Assistant.Core.Confirmations;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Permissions;

internal sealed class PermissionEngine(
    IPermissionPolicy policy,
    IPendingConfirmationStore confirmations,
    ILogger<PermissionEngine> log) : IPermissionEngine
{
    public async ValueTask<PermissionDecision> EvaluateAsync(
        ToolDescriptor tool, ToolArguments args, ToolExecutionContext context, CancellationToken ct)
    {
        // ADR-0009, rule 3. BEFORE the policy on purpose: no policy,
        // not even one written in a hurry in V8, can loosen this.
        if (!Enum.IsDefined(tool.Level) || tool.Level is PermissionLevel.Undeclared)
        {
            CoreLog.ToolMissingLevel(log, tool.Name, tool.ExtensionName);

            return PermissionDecision.Deny("La tool no declara su nivel de permiso.");
        }

        if (context.ConfirmationId is { } confirmationId)
        {
            return await EvaluateConfirmationAsync(tool, confirmationId, context, ct);
        }

        var decision = await policy.EvaluateAsync(tool, args, context, ct);

        // ADR-0018 rule 3: a durable write in a contaminated turn needs the existing card.
        // Replay already returned above, so an approved ConfirmationId stays Allow.
        if (decision.Outcome is PermissionOutcome.Allow
            && tool.WritesDurableState
            && context.TurnContaminated)
        {
            decision = PermissionDecision.Confirm(
                $"Este turno incluye contenido no confiable. ¿Autorizás que ejecute «{tool.Name}»?");
        }

        // A confirmation without text would leave the client with nothing to show.
        return decision is { Outcome: PermissionOutcome.RequireConfirmation, ConfirmationPrompt: null }
            ? decision with { ConfirmationPrompt = $"¿Autorizás que ejecute «{tool.Name}»?" }
            : decision;
    }

    private async ValueTask<PermissionDecision> EvaluateConfirmationAsync(
        ToolDescriptor tool,
        Guid confirmationId,
        ToolExecutionContext context,
        CancellationToken ct)
    {
        var pending = await confirmations.FindAsync(
            confirmationId, context.ConversationId, context.UserId, ct);

        // Claim runs before InvokeAsync (T-08.4). Only the winner reaches the dispatcher
        // with ConfirmationId, so Allow requires an Approved row that still matches.
        if (pending is null
            || !string.Equals(pending.ToolName, tool.Name, StringComparison.Ordinal)
            || pending.ResolvedOutcome is not ConfirmationResolutionOutcome.Approved)
        {
            return PermissionDecision.Deny("La confirmación no es válida o ya no está vigente.");
        }

        return PermissionDecision.Allow();
    }
}
