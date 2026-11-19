using System.Security.Claims;
using Assistant.Abstractions.Proactive;
using Assistant.Api.Authentication;
using Assistant.Extensions.Proactive;

namespace Assistant.Api.Endpoints;

internal static class DiaryEndpoints
{
    public static IEndpointRouteBuilder MapDiaryEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/diary", ListAsync);
        group.MapPost("/diary/{proposalId:guid}/approve", ApproveAsync);
        group.MapPost("/diary/{proposalId:guid}/reject", RejectAsync);
        group.MapPost("/diary/{entryId:guid}/undo", UndoAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        IDiaryStore diary,
        CancellationToken ct)
    {
        var entries = await diary.ListAsync(user.UserId(), ct);
        return Results.Ok(new DiaryListResponse(entries.Select(ToResponse).ToList()));
    }

    private static async Task<IResult> ApproveAsync(
        Guid proposalId,
        ClaimsPrincipal user,
        ProposalApproval approval,
        CancellationToken ct)
        => ToResult(await approval.ApproveAsync(user.UserId(), proposalId, ct));

    private static async Task<IResult> RejectAsync(
        Guid proposalId,
        ClaimsPrincipal user,
        ProposalApproval approval,
        CancellationToken ct)
        => ToResult(await approval.RejectAsync(user.UserId(), proposalId, ct));

    private static async Task<IResult> UndoAsync(
        Guid entryId,
        ClaimsPrincipal user,
        ProposalApproval approval,
        CancellationToken ct)
        => ToResult(await approval.UndoAsync(user.UserId(), entryId, ct));

    private static IResult ToResult(ProposalApprovalResult result)
    {
        if (result.Outcome == ProposalApprovalOutcomes.NotFound)
        {
            return Results.Problem(title: "Diary entry not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (result.Outcome == ProposalApprovalOutcomes.Conflict)
        {
            return Results.Problem(
                title: "The diary entry cannot be changed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(new DiaryActionResponse(result.Outcome, result.ToolName));
    }

    private static DiaryEntryResponse ToResponse(DiaryEntry entry)
        => new(
            entry.Id,
            entry.ProposalId,
            entry.PreviousId,
            entry.SuccessorId,
            entry.Reason,
            entry.Outcome,
            entry.ProposalStatus,
            entry.CreatedAt);

    private sealed record DiaryListResponse(IReadOnlyList<DiaryEntryResponse> Entries);

    private sealed record DiaryEntryResponse(
        Guid Id,
        Guid? ProposalId,
        Guid? PreviousId,
        Guid? SuccessorId,
        string Reason,
        string Outcome,
        string? ProposalStatus,
        DateTimeOffset CreatedAt);

    private sealed record DiaryActionResponse(string Outcome, string? ToolName);
}
