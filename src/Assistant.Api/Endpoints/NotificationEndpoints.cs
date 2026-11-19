using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Time;
using Assistant.Api.Authentication;
using Assistant.Channels.Delivery;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Endpoints;

internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/notifications", ListRecentAsync);
        group.MapPost("/notifications/{id:guid}/ack", AckAsync);
        group.MapPost("/notifications/{id:guid}/actions", ActAsync);
        group.MapPost("/notifications/test", PublishTestAsync);
        return group;
    }

    private static async Task<IResult> ListRecentAsync(
        ClaimsPrincipal user,
        INotificationStore store,
        IUserTimeZoneProvider timeZones,
        int? limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 50);
        var zone = await timeZones.GetTimeZoneAsync(user.UserId(), ct);
        var rows = await store.ListRecentAsync(user.UserId(), take, ct);
        return Results.Ok(new NotificationLogResponse(
            rows.Select(row => ToLogItem(row, zone)).ToList()));
    }

    private static async Task<IResult> ActAsync(
        Guid id,
        NotificationActionBody? body,
        ClaimsPrincipal user,
        INotificationActionService actions,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Action))
        {
            return Problem(StatusCodes.Status400BadRequest, "Action is required.", "invalid_action");
        }

        var result = await actions.ExecuteAsync(user.UserId(), id, body.Action, ct);
        return result.Status switch
        {
            NotificationActionStatus.Applied => Results.NoContent(),
            NotificationActionStatus.SnoozeLimit => Problem(
                StatusCodes.Status409Conflict, "Ya lo pospusiste bastante.", "snooze_limit"),
            NotificationActionStatus.AlreadyActed => Problem(
                StatusCodes.Status409Conflict, "Ese aviso ya tuvo una respuesta.", "already_acted"),
            NotificationActionStatus.Unsupported => Problem(
                StatusCodes.Status422UnprocessableEntity, "Esa acción no está disponible.", "unsupported_action"),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> AckAsync(
        Guid id,
        ClaimsPrincipal user,
        INotificationStore store,
        IDeliveryAckWait acks,
        CancellationToken ct)
    {
        var notification = await store.FindAsync(id, ct);
        if (notification is null || notification.UserId != user.UserId())
        {
            return Results.NotFound();
        }

        acks.TryCompleteNotification(id);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishTestAsync(
        ClaimsPrincipal user,
        INotificationPublisher publisher,
        IOptions<DiagnosticsOptions> diagnostics,
        CancellationToken ct)
    {
        if (!diagnostics.Value.EnableTestEndpoints)
        {
            return Results.NotFound();
        }

        var receipt = await publisher.PublishAsync(
            new NotificationRequest(
                user.UserId(),
                NotificationKind.System,
                $"test:{Guid.CreateVersion7():N}",
                "Aviso de prueba",
                "Si ves esto, el hub entregó la notificación.",
                AllowDuringQuietHours: true),
            ct);

        return Results.Ok(new TestNotificationResponse(receipt.NotificationId, receipt.Created));
    }

    private static NotificationLogItemResponse ToLogItem(NotificationLogItem item, TimeZoneInfo zone)
        => new(
            item.Id,
            item.Title,
            item.Body,
            item.Status,
            item.Channel,
            item.Action,
            TimeZoneInfo.ConvertTime(item.CreatedAt, zone)
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));

    private static IResult Problem(int status, string title, string code)
        => Results.Problem(
            title: title,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record NotificationLogResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<NotificationLogItemResponse> Items);

    private sealed record NotificationLogItemResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("channel")] string? Channel,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("local_at")] string LocalAt);

    private sealed record NotificationActionBody(string? Action);

    private sealed record TestNotificationResponse(Guid NotificationId, bool Created);
}

/// <summary><c>Diagnostics</c> section. Test routes stay off unless this flag is true.</summary>
internal sealed class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";

    public bool EnableTestEndpoints { get; set; }
}
