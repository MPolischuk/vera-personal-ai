using System.Security.Claims;
using Assistant.Api.Authentication;
using Assistant.Core.Conversations;

namespace Assistant.Api.Endpoints;

internal static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/conversations", ListAsync);
        group.MapPost("/conversations", CreateAsync);
        group.MapGet("/conversations/{id:guid}", FindAsync);
        group.MapGet("/conversations/{id:guid}/messages", HistoryAsync);
        group.MapDelete("/conversations/{id:guid}", DeleteAsync);
        group.MapDelete("/conversations", DeleteAllAsync);

        return group;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        IConversationStore store,
        int? skip,
        int? take,
        CancellationToken ct)
    {
        var conversations = await store.ListAsync(
            user.UserId(), Math.Max(skip ?? 0, 0), Math.Clamp(take ?? 50, 1, 100), ct);

        return Results.Ok(conversations.Select(ToResponse));
    }

    private static async Task<IResult> CreateAsync(
        ClaimsPrincipal user,
        CreateConversationRequest request,
        IConversationStore store,
        CancellationToken ct)
    {
        var conversation = await store.CreateAsync(user.UserId(), request.Title, ct);
        return Results.Created($"/api/conversations/{conversation.Id:D}", ToResponse(conversation));
    }

    private static async Task<IResult> FindAsync(
        Guid id,
        ClaimsPrincipal user,
        IConversationStore store,
        CancellationToken ct)
    {
        var conversation = await store.FindAsync(id, user.UserId(), ct);
        return conversation is null
            ? NotFound()
            : Results.Ok(ToResponse(conversation));
    }

    private static async Task<IResult> HistoryAsync(
        Guid id,
        ClaimsPrincipal user,
        IConversationStore store,
        int? take,
        CancellationToken ct)
    {
        if (await store.FindAsync(id, user.UserId(), ct) is null)
        {
            return NotFound();
        }

        var messages = await store.GetHistoryAsync(id, user.UserId(), Math.Clamp(take ?? 100, 1, 500), ct);
        return Results.Ok(messages.Select(ToResponse));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        IConversationStore store,
        CancellationToken ct)
        => await store.SoftDeleteAsync(id, user.UserId(), ct)
            ? Results.NoContent()
            : NotFound();

    private static async Task<IResult> DeleteAllAsync(
        ClaimsPrincipal user,
        IConversationStore store,
        CancellationToken ct)
    {
        var userId = user.UserId();
        while (true)
        {
            var batch = await store.ListAsync(userId, skip: 0, take: 100, ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var conversation in batch)
            {
                await store.SoftDeleteAsync(conversation.Id, userId, ct);
            }
        }

        return Results.NoContent();
    }

    private static IResult NotFound()
        => Results.Problem(
            title: "Conversation not found.",
            statusCode: StatusCodes.Status404NotFound);

    private static ConversationResponse ToResponse(Conversation conversation)
        => new(
            conversation.Id,
            conversation.Title,
            conversation.StartedAt,
            conversation.LastMessageAt);

    private static MessageResponse ToResponse(ConversationMessage message)
        => new(
            message.Id,
            message.ConversationId,
            message.Role.ToString(),
            message.Content,
            message.Sequence,
            message.WasInterrupted,
            message.Metadata,
            message.CreatedAt);

    private sealed record CreateConversationRequest(string? Title);

    private sealed record ConversationResponse(
        Guid Id,
        string? Title,
        DateTimeOffset StartedAt,
        DateTimeOffset LastMessageAt);

    private sealed record MessageResponse(
        Guid Id,
        Guid ConversationId,
        string Role,
        string Content,
        int Sequence,
        bool WasInterrupted,
        string? Metadata,
        DateTimeOffset CreatedAt);
}
