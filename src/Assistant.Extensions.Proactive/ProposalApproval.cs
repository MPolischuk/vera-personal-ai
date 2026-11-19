using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tools;
using Assistant.Core.Memory;
using Assistant.Core.Permissions;
using Assistant.Core.Tools;

namespace Assistant.Extensions.Proactive;

public static class ProposalApprovalOutcomes
{
    public const string Approved = "approved";

    public const string RejectedVersion = "rejected_version";

    public const string Rejected = "rejected";

    public const string ConfirmationRequired = "confirmation_required";

    public const string Undone = "undone";

    public const string NotFound = "not_found";

    public const string Conflict = "conflict";
}

public sealed record ProposalApprovalResult(string Outcome, string? ToolName);

/// <summary>
/// Applies a diary decision. A <see cref="PermissionLevel.SensitiveAction"/> stays pending:
/// the dispatcher is not called and no confirmation id is created.
/// </summary>
public sealed class ProposalApproval(
    IDiaryStore diary,
    IMemoryStore memories,
    IToolRegistry tools,
    IToolDispatcher dispatcher,
    TimeProvider clock)
{
    public async ValueTask<ProposalApprovalResult> ApproveAsync(
        Guid userId, Guid proposalId, CancellationToken ct)
    {
        var proposal = await diary.FindProposalAsync(proposalId, userId, ct);
        if (proposal is null)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.NotFound, null);
        }

        if (proposal.Status != ProposalStatuses.Pending)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, null);
        }

        if (proposal.Origin == ProposalOrigins.Consolidation)
        {
            return await ApproveConsolidationAsync(userId, proposal, ct);
        }

        if (proposal.Origin == ProposalOrigins.Heartbeat)
        {
            return await ApproveHeartbeatAsync(userId, proposal, ct);
        }

        return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, null);
    }

    public async ValueTask<ProposalApprovalResult> RejectAsync(
        Guid userId, Guid proposalId, CancellationToken ct)
    {
        var proposal = await diary.FindProposalAsync(proposalId, userId, ct);
        if (proposal is null)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.NotFound, null);
        }

        if (proposal.Status != ProposalStatuses.Pending)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, null);
        }

        var updated = await diary.TrySetProposalStatusAsync(
            proposal.Id, userId, ProposalStatuses.Rejected, clock.GetUtcNow(), ct);
        return new ProposalApprovalResult(
            updated ? ProposalApprovalOutcomes.Rejected : ProposalApprovalOutcomes.Conflict,
            null);
    }

    public async ValueTask<ProposalApprovalResult> UndoAsync(
        Guid userId, Guid entryId, CancellationToken ct)
    {
        var entry = await diary.FindAsync(entryId, userId, ct);
        if (entry is null)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.NotFound, null);
        }

        if (entry.Outcome != DiaryOutcomes.Approved
            || entry.PreviousId is not Guid previousId
            || entry.SuccessorId is not Guid successorId)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, null);
        }

        var cleared = await memories.ClearSupersessionAsync(previousId, userId, successorId, ct);
        if (!cleared)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, null);
        }

        await diary.AppendAsync(
            new NewDiaryEntry(
                userId,
                entry.ProposalId,
                previousId,
                successorId,
                entry.Reason,
                DiaryOutcomes.Undone),
            ct);
        return new ProposalApprovalResult(ProposalApprovalOutcomes.Undone, null);
    }

    private async ValueTask<ProposalApprovalResult> ApproveConsolidationAsync(
        Guid userId, Proposal proposal, CancellationToken ct)
    {
        if (proposal.SubjectUpdatedAt is not DateTimeOffset expected
            || !ContradictionParser.TryReadStoredPair(proposal.ArgumentsJson, out var previousId, out var successorId))
        {
            await diary.TrySetProposalStatusAsync(
                proposal.Id, userId, ProposalStatuses.Rejected, clock.GetUtcNow(), ct);
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Rejected, null);
        }

        var status = await memories.SupersedeAsync(
            previousId, successorId, userId, expected, clock.GetUtcNow(), ct);
        if (status == MemorySupersedeStatus.Missing)
        {
            await diary.TrySetProposalStatusAsync(
                proposal.Id, userId, ProposalStatuses.Rejected, clock.GetUtcNow(), ct);
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Rejected, null);
        }

        if (status == MemorySupersedeStatus.VersionMismatch)
        {
            await diary.TrySetProposalStatusAsync(
                proposal.Id, userId, ProposalStatuses.Rejected, clock.GetUtcNow(), ct);
            await diary.AppendAsync(
                new NewDiaryEntry(
                    userId,
                    proposal.Id,
                    previousId,
                    successorId,
                    Clip(proposal.Reason),
                    DiaryOutcomes.RejectedVersion),
                ct);
            return new ProposalApprovalResult(ProposalApprovalOutcomes.RejectedVersion, null);
        }

        await diary.TrySetProposalStatusAsync(
            proposal.Id, userId, ProposalStatuses.Approved, clock.GetUtcNow(), ct);
        await diary.AppendAsync(
            new NewDiaryEntry(
                userId,
                proposal.Id,
                previousId,
                successorId,
                Clip(proposal.Reason),
                DiaryOutcomes.Approved),
            ct);
        return new ProposalApprovalResult(ProposalApprovalOutcomes.Approved, null);
    }

    private async ValueTask<ProposalApprovalResult> ApproveHeartbeatAsync(
        Guid userId, Proposal proposal, CancellationToken ct)
    {
        if (!tools.TryGetDescriptor(proposal.ToolName, out var descriptor))
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, proposal.ToolName);
        }

        if (descriptor.Level == PermissionLevel.SensitiveAction)
        {
            return new ProposalApprovalResult(
                ProposalApprovalOutcomes.ConfirmationRequired,
                proposal.ToolName);
        }

        if (descriptor.Level != PermissionLevel.ReversibleAction)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, proposal.ToolName);
        }

        var arguments = string.IsNullOrWhiteSpace(proposal.ArgumentsJson)
            ? ToolArguments.Empty
            : ToolArguments.FromJson(proposal.ArgumentsJson);
        var result = await dispatcher.InvokeAsync(
            proposal.ToolName,
            arguments,
            new ToolExecutionContext(
                userId,
                Guid.Empty,
                Guid.CreateVersion7(),
                ConfirmationId: null,
                TurnContaminated: false),
            ct);
        if (result.Permission != PermissionOutcome.Allow)
        {
            return new ProposalApprovalResult(ProposalApprovalOutcomes.Conflict, proposal.ToolName);
        }

        await diary.TrySetProposalStatusAsync(
            proposal.Id, userId, ProposalStatuses.Approved, clock.GetUtcNow(), ct);
        return new ProposalApprovalResult(ProposalApprovalOutcomes.Approved, proposal.ToolName);
    }

    private static string Clip(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.Length <= ContradictionParser.ReasonMaxLength
            ? trimmed
            : trimmed[..ContradictionParser.ReasonMaxLength];
    }
}
