using System.Security.Claims;
using Assistant.Api.Authentication;
using Assistant.Core.Usage;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Endpoints;

internal static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/usage", GetAsync);
        return group;
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IUsageStore store,
        IOptions<CostOptions> costs,
        DateTimeOffset? since,
        TimeProvider clock,
        CancellationToken ct)
    {
        var start = since ?? MonthStart(clock.GetUtcNow());
        var summary = await store.GetSummaryAsync(user.UserId(), start, ct);

        return Results.Ok(new UsageResponse(
            summary.Since,
            summary.Turns,
            summary.InputTokens,
            summary.OutputTokens,
            summary.EstimatedCostUsd,
            costs.Value.MonthlyBudgetUsd,
            costs.Value.WarnAtPercent,
            summary.ByModel.Select(ToResponse)));
    }

    private static DateTimeOffset MonthStart(DateTimeOffset now)
        => new(now.Year, now.Month, 1, 0, 0, 0, now.Offset);

    private static UsageByModelResponse ToResponse(UsageByModel usage)
        => new(
            usage.Provider,
            usage.Model,
            usage.InputTokens,
            usage.OutputTokens,
            usage.EstimatedCostUsd);

    private sealed record UsageResponse(
        DateTimeOffset Since,
        int Turns,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCostUsd,
        decimal MonthlyBudgetUsd,
        int WarnAtPercent,
        IEnumerable<UsageByModelResponse> ByModel);

    private sealed record UsageByModelResponse(
        string Provider,
        string Model,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCostUsd);
}
