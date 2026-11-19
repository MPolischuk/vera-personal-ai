using System.Security.Claims;
using System.Text.Json.Serialization;
using Assistant.Abstractions.Reminders;
using Assistant.Api.Authentication;
using Assistant.Core.Audit;
using Assistant.Extensions.Reminders.Time;

namespace Assistant.Api.Endpoints;

internal static class ReminderEndpoints
{
    private const int ActiveLimit = 100;
    private static readonly TimeSpan MissedWindow = TimeSpan.FromDays(30);

    public static IEndpointRouteBuilder MapReminderEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/reminders", ListAsync);
        group.MapDelete("/reminders/{id:guid}", CancelAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        ClaimsPrincipal user,
        IReminderQueryService query,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (status is not ("active" or "missed"))
        {
            return Problem(StatusCodes.Status400BadRequest, "Status must be active or missed.", "invalid_status");
        }

        var scope = status == "missed" ? ReminderListScope.Missed : ReminderListScope.Active;
        var limit = status == "missed" ? 200 : ActiveLimit;
        var rows = await query.ListAsync(user.UserId(), scope, limit, ct);
        if (status == "missed")
        {
            var cutoff = clock.GetUtcNow() - MissedWindow;
            rows = rows.Where(row => row.UpdatedAt >= cutoff).ToList();
        }

        var now = clock.GetUtcNow();
        return Results.Ok(new ReminderListResponse(rows.Select(row => ToItem(row, now)).ToList()));
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        IReminderStore store,
        IAuditLogStore audit,
        CancellationToken ct)
    {
        var userId = user.UserId();
        var cancelled = await store.CancelAsync(userId, id, ct);
        switch (cancelled)
        {
            case CancelReminderResult.Cancelled:
                await audit.AppendAsync(
                    new AuditEntry(
                        userId,
                        "reminder.cancelled",
                        "reminder",
                        id,
                        """{"source":"web"}"""),
                    ct);
                return Results.NoContent();
            case CancelReminderResult.Rejected { Reason: ReminderRejection.NotActive }:
                return Problem(StatusCodes.Status409Conflict, "The reminder is not active.", "not_active");
            default:
                return Results.NotFound();
        }
    }

    private static ReminderItemResponse ToItem(ReminderListItem item, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(item.TimeZone, out var found)
            ? found
            : TimeZoneInfo.Utc;
        var nowLocal = RecurrenceDescriber.ToCivil(now, zone);
        string localDue;
        DateTime dueLocal;
        if (item.NextOccurrenceAt is DateTimeOffset next)
        {
            localDue = RecurrenceDescriber.FormatOffset(next, zone);
            dueLocal = RecurrenceDescriber.ToCivil(next, zone);
        }
        else
        {
            localDue = RecurrenceDescriber.FormatCivil(item.LocalStart, zone);
            dueLocal = item.LocalStart;
        }

        return new ReminderItemResponse(
            item.Id,
            item.Text,
            item.Status,
            localDue,
            RecurrenceDescriber.Relative(dueLocal, nowLocal),
            RecurrenceDescriber.Describe(item.RecurrenceRule, item.LocalStart, zone),
            item.OriginChannel);
    }

    private static IResult Problem(int status, string title, string code)
        => Results.Problem(
            title: title,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record ReminderListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<ReminderItemResponse> Items);

    private sealed record ReminderItemResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("local_due")] string LocalDue,
        [property: JsonPropertyName("relative")] string Relative,
        [property: JsonPropertyName("recurrence")] string? Recurrence,
        [property: JsonPropertyName("channel")] string Channel);
}
