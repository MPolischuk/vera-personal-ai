using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Assistant.Api.Authentication;
using Assistant.Core.Audit;
using Assistant.Core.Skills;
using Assistant.Extensions.Skills;

namespace Assistant.Api.Endpoints;

internal static class SkillProposalEndpoints
{
    public static IEndpointRouteBuilder MapSkillProposalEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/skills/proposals", ListAsync);
        group.MapPost("/skills/proposals/{id:guid}/approve", ApproveAsync);
        group.MapPost("/skills/proposals/{id:guid}/discard", DiscardAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        ISkillProposalStore proposals,
        SkillFileWriter files,
        CancellationToken ct)
    {
        var pending = await proposals.ListPendingAsync(user.UserId(), ct);
        var items = pending.Select(proposal =>
        {
            var diff = SkillDiff.Format(
                proposal.Name, files.ReadCurrent(proposal.Name), proposal.Content);
            return new SkillProposalResponse(
                proposal.Id,
                proposal.Name,
                proposal.Description,
                proposal.CreatedAt,
                diff,
                DiffSha256(diff));
        }).ToList();
        return Results.Ok(items);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        ApproveSkillRequest? request,
        ClaimsPrincipal user,
        ISkillProposalStore proposals,
        SkillFileWriter files,
        IAuditLogStore audit,
        CancellationToken ct)
    {
        var userId = user.UserId();
        var proposal = await proposals.FindAsync(id, userId, ct);
        if (proposal is null)
        {
            return NotFound();
        }

        if (proposal.Status is not SkillProposalStatus.Pending)
        {
            return NoLongerPending();
        }

        var diffSha256 = request?.DiffSha256;
        if (string.IsNullOrWhiteSpace(diffSha256))
        {
            return Results.Problem(
                title: "A diff hash is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var diff = SkillDiff.Format(
            proposal.Name, files.ReadCurrent(proposal.Name), proposal.Content);
        if (!string.Equals(diffSha256, DiffSha256(diff), StringComparison.Ordinal))
        {
            return Results.Problem(
                title: "The skill diff has changed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (!SkillParser.TryParse(proposal.Name, proposal.Content, out _, out _))
        {
            return Results.Problem(
                title: "The skill text is no longer valid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await files.WriteAsync(proposal.Name, proposal.Content, ct);
        if (!await proposals.TryResolveAsync(id, userId, SkillProposalStatus.Approved, ct))
        {
            return NoLongerPending();
        }

        await audit.AppendAsync(
            new AuditEntry(userId, "skill_proposal.approved", "skill_proposal", id, NamePayload(proposal.Name)),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DiscardAsync(
        Guid id,
        ClaimsPrincipal user,
        ISkillProposalStore proposals,
        IAuditLogStore audit,
        CancellationToken ct)
    {
        var userId = user.UserId();
        var proposal = await proposals.FindAsync(id, userId, ct);
        if (proposal is null)
        {
            return NotFound();
        }

        if (proposal.Status is not SkillProposalStatus.Pending)
        {
            return NoLongerPending();
        }

        if (!await proposals.TryResolveAsync(id, userId, SkillProposalStatus.Discarded, ct))
        {
            return NoLongerPending();
        }

        await audit.AppendAsync(
            new AuditEntry(userId, "skill_proposal.discarded", "skill_proposal", id, NamePayload(proposal.Name)),
            ct);
        return Results.NoContent();
    }

    private static IResult NotFound()
        => Results.Problem(
            title: "Skill proposal not found.",
            statusCode: StatusCodes.Status404NotFound);

    private static IResult NoLongerPending()
        => Results.Problem(
            title: "The skill proposal is no longer pending.",
            statusCode: StatusCodes.Status409Conflict);

    private static string NamePayload(string name)
        => JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = name });

    private static string DiffSha256(string diff)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(diff)));

    private sealed record SkillProposalResponse(
        Guid Id,
        string Name,
        string Description,
        DateTimeOffset CreatedAt,
        string Diff,
        string DiffSha256);

    private sealed record ApproveSkillRequest(string? DiffSha256);
}
