using System.Security.Claims;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Time;
using Assistant.Api.Authentication;

namespace Assistant.Api.Endpoints;

internal static class BriefingEndpoints
{
    public static IEndpointRouteBuilder MapBriefingEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/briefings/today", TodayAsync);
        group.MapPost("/briefings/{briefingId:guid}/items/{itemId:guid}/feedback", FeedbackAsync);
        return group;
    }

    private static async Task<IResult> TodayAsync(
        ClaimsPrincipal user,
        IBriefingStore briefings,
        IUserTimeZoneProvider timeZones,
        TimeProvider clock,
        CancellationToken ct)
    {
        var zone = await ResolveZoneAsync(timeZones, user.UserId(), ct);
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var briefing = await briefings.FindByLocalDateAsync(user.UserId(), local, ct);
        return Results.Ok(briefing is null ? null : ToResponse(briefing));
    }

    private static async Task<IResult> FeedbackAsync(
        Guid briefingId,
        Guid itemId,
        FeedbackRequest request,
        ClaimsPrincipal user,
        IBriefingStore briefings,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!BriefingFeedback.IsKnown(request.Feedback))
        {
            return Results.Problem(
                title: "The feedback value is invalid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var saved = await briefings.SetFeedbackAsync(
            user.UserId(),
            briefingId,
            itemId,
            request.Feedback!,
            clock.GetUtcNow(),
            ct);
        return saved
            ? Results.NoContent()
            : Results.Problem(title: "Briefing item not found.", statusCode: StatusCodes.Status404NotFound);
    }

    private static async ValueTask<TimeZoneInfo> ResolveZoneAsync(
        IUserTimeZoneProvider timeZones, Guid userId, CancellationToken ct)
    {
        try
        {
            return await timeZones.GetTimeZoneAsync(userId, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BriefingClock.Zone;
        }
    }

    private static BriefingResponse ToResponse(Briefing briefing)
        => new(
            briefing.Id,
            briefing.LocalDate,
            briefing.Summary,
            Items(briefing, BriefingBlocks.Today),
            Items(briefing, BriefingBlocks.Ahead));

    private static List<BriefingItemResponse> Items(Briefing briefing, string block)
        => briefing.Items
            .Where(item => item.Block == block)
            .Select(item => new BriefingItemResponse(
                item.Id,
                item.Title,
                item.Reason,
                item.Triage,
                item.Source,
                item.SourceRef,
                item.Feedback))
            .ToList();

    private sealed record FeedbackRequest(string? Feedback);

    private sealed record BriefingResponse(
        Guid Id,
        DateOnly LocalDate,
        string Summary,
        IReadOnlyList<BriefingItemResponse> Today,
        IReadOnlyList<BriefingItemResponse> Ahead);

    private sealed record BriefingItemResponse(
        Guid Id,
        string Title,
        string Reason,
        string Triage,
        string Source,
        string? SourceRef,
        string? Feedback);
}
