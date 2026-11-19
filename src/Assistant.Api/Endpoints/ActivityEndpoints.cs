using System.Security.Claims;
using System.Text.Json.Serialization;
using Assistant.Abstractions.Tasks;
using Assistant.Api.Authentication;
using Assistant.Extensions.Delegation;

namespace Assistant.Api.Endpoints;

internal static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/activity", GetAsync);
        group.MapPost("/activity/{id:guid}/cancel", CancelAsync);
        return group;
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IAgentTaskStore tasks,
        CancellationToken ct)
    {
        var page = await tasks.GetActivityAsync(user.UserId(), ct);
        return Results.Ok(new ActivityResponse(
            page.Tasks.Select(task => new ActivityTaskResponse(
                task.Id,
                task.Kind,
                task.State,
                task.FailureReason,
                task.CostUsd,
                task.CreatedAt,
                task.UpdatedAt,
                task.CompletedAt,
                task.Partial)).ToList(),
            page.Tools.Select(tool => new ActivityToolResponse(
                tool.Name,
                tool.Extension,
                tool.Outcome,
                tool.At)).ToList()));
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        IServiceProvider services,
        CancellationToken ct)
    {
        var cancelled = await AgentTaskCancellation.TryCancelAsync(services, id, user.UserId(), ct);
        return cancelled ? Results.NoContent() : Results.NotFound();
    }

    private sealed record ActivityResponse(
        [property: JsonPropertyName("tasks")] IReadOnlyList<ActivityTaskResponse> Tasks,
        [property: JsonPropertyName("tools")] IReadOnlyList<ActivityToolResponse> Tools);

    private sealed record ActivityTaskResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("failure_reason")] string? FailureReason,
        [property: JsonPropertyName("cost_usd")] decimal CostUsd,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
        [property: JsonPropertyName("partial")] string? Partial);

    private sealed record ActivityToolResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("extension")] string Extension,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("at")] DateTimeOffset At);
}
