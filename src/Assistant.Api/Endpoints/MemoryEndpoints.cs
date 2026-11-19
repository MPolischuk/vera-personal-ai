using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Api.Authentication;
using Assistant.Core.Audit;
using Assistant.Core.Events;
using Assistant.Core.Memory;
using Assistant.Extensions.Memory;

namespace Assistant.Api.Endpoints;

internal static class MemoryEndpoints
{
    private const int PageSize = 500;

    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/memories/export", ExportAsync);
        group.MapGet("/memories/journey", GetJourneyAsync);
        group.MapGet("/memories", ListMemoriesAsync);
        group.MapPut("/memories/{id:guid}/pinned", UnpinMemoryAsync);
        group.MapDelete("/memories/{id:guid}", DeleteMemoryAsync);
        group.MapPost("/memories/{id:guid}/restore", RestoreMemoryAsync);
        group.MapGet("/preferences", ListPreferencesAsync);
        group.MapPut("/preferences/{key}", UpsertPreferenceAsync);
        group.MapDelete("/preferences/{key}", DeletePreferenceAsync);
        return group;
    }

    private static async Task<IResult> GetJourneyAsync(
        ClaimsPrincipal user,
        IMemoryStore memories,
        CancellationToken ct)
    {
        var journey = await memories.ListJourneyAsync(user.UserId(), ct);
        return Results.Ok(new MemoryJourneyResponse(
            journey.Current.Select(ToJourneyItem).ToList(),
            journey.Replaced.Select(ToJourneyItem).ToList(),
            journey.Forgotten.Select(ToJourneyItem).ToList()));
    }

    private static async Task<IResult> ListMemoriesAsync(
        ClaimsPrincipal user,
        IMemoryStore memories,
        int? skip,
        int? take,
        CancellationToken ct)
    {
        var effectiveSkip = Math.Max(skip ?? 0, 0);
        var effectiveTake = Math.Clamp(take ?? 50, 1, 100);
        var page = await memories.ListAsync(user.UserId(), effectiveSkip, effectiveTake, ct);
        return Results.Ok(new MemoryListResponse(
            page.Select(ToItem).ToList(),
            effectiveSkip,
            effectiveTake));
    }

    private static async Task<IResult> DeleteMemoryAsync(
        Guid id,
        ClaimsPrincipal user,
        IMemoryStore memories,
        IAuditLogStore audit,
        IEventBus events,
        CancellationToken ct)
    {
        var userId = user.UserId();
        if (await memories.FindAsync(id, userId, ct) is null)
        {
            return Results.Problem(
                title: "Memory not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await memories.ForgetAsync(id, userId, ct);
        await audit.AppendAsync(
            new AuditEntry(userId, "memory.forgotten", "memory", id, """{"source":"rest"}"""),
            ct);
        await events.PublishAsync(
            new MemoryForgotten(userId, id, Guid.Empty, Guid.Empty),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreMemoryAsync(
        Guid id,
        ClaimsPrincipal user,
        IMemoryStore memories,
        TimeProvider clock,
        CancellationToken ct)
    {
        var status = await memories.RestoreAsync(
            id,
            user.UserId(),
            clock.GetUtcNow() - MemoryPurgeService.GracePeriod,
            ct);

        return status switch
        {
            MemoryRestoreStatus.Restored => Results.NoContent(),
            MemoryRestoreStatus.Expired => Results.Problem(
                title: "The memory can no longer be restored.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(
                title: "Memory not found.",
                statusCode: StatusCodes.Status404NotFound),
        };
    }

    private static async Task<IResult> UnpinMemoryAsync(
        Guid id,
        SetPinnedRequest request,
        ClaimsPrincipal user,
        IMemoryStore memories,
        IAuditLogStore audit,
        CancellationToken ct)
    {
        if (request.Pinned is not false)
        {
            return Results.Problem(
                title: "Only unpinning is supported.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = user.UserId();
        if (!await memories.SetPinnedAsync(id, userId, pinned: false, ct))
        {
            return Results.Problem(
                title: "Memory not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        await audit.AppendAsync(
            new AuditEntry(userId, "memory.unpinned", "memory", id, """{"source":"rest"}"""),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPreferencesAsync(
        ClaimsPrincipal user,
        IPreferenceStore preferences,
        CancellationToken ct)
    {
        var items = await preferences.ListAsync(user.UserId(), ct);
        return Results.Ok(items.Select(ToItem).ToList());
    }

    private static async Task<IResult> UpsertPreferenceAsync(
        string key,
        UpsertPreferenceRequest request,
        ClaimsPrincipal user,
        IPreferenceStore preferences,
        IEventBus events,
        CancellationToken ct)
    {
        var normalized = key.Trim();
        if (!PreferenceKeys.IsValid(normalized))
        {
            return Results.Problem(
                title: "The preference key is invalid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var value = request.Value?.Trim() ?? "";
        if (value.Length == 0)
        {
            return Results.Problem(
                title: "The preference value is empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = user.UserId();
        var item = await preferences.UpsertAsync(userId, normalized, value, WriteProvenance.User, ct);
        await events.PublishAsync(
            new PreferenceUpdated(userId, normalized, Guid.Empty, Guid.Empty),
            ct);
        return Results.Ok(ToItem(item));
    }

    private static async Task<IResult> DeletePreferenceAsync(
        string key,
        ClaimsPrincipal user,
        IPreferenceStore preferences,
        CancellationToken ct)
    {
        var normalized = key.Trim();
        if (!PreferenceKeys.IsValid(normalized))
        {
            return Results.Problem(
                title: "The preference key is invalid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var deleted = await preferences.DeleteAsync(user.UserId(), normalized, ct);
        return deleted
            ? Results.NoContent()
            : Results.Problem(
                title: "Preference not found.",
                statusCode: StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ExportAsync(
        ClaimsPrincipal user,
        IMemoryStore memories,
        IPreferenceStore preferences,
        TimeProvider clock,
        CancellationToken ct)
    {
        var userId = user.UserId();
        var exportedMemories = new List<MemoryExportItem>();
        for (var skip = 0; ; skip += PageSize)
        {
            var page = await memories.ListAsync(userId, skip, PageSize, ct);
            exportedMemories.AddRange(page.Select(ToItem));
            if (page.Count < PageSize)
            {
                break;
            }
        }

        var exportedPreferences = await preferences.ListAsync(userId, ct);

        return Results.Ok(new MemoryExportResponse(
            clock.GetUtcNow(),
            userId,
            exportedMemories,
            exportedPreferences.Select(ToItem).ToList()));
    }

    private static MemoryJourneyItemResponse ToJourneyItem(MemoryJourneyItem item)
        => new(
            item.Id,
            item.UserId,
            item.Kind.ToString().ToLowerInvariant(),
            item.Content,
            item.CreatedAt,
            Provenance(item.WriteProvenance),
            item.Pinned,
            item.IndexTerms,
            item.SupersededBy);

    private static MemoryExportItem ToItem(MemoryItem item)
        => new(
            item.Id,
            item.UserId,
            item.Kind.ToString().ToLowerInvariant(),
            item.Content,
            item.SourceConversationId,
            item.DueAt,
            item.CreatedAt,
            item.UpdatedAt,
            Provenance(item.WriteProvenance),
            item.Pinned,
            item.IndexTerms,
            item.DerivedFrom);

    private static PreferenceExportItem ToItem(PreferenceItem item)
        => new(
            item.Id,
            item.UserId,
            item.Key,
            item.Value,
            item.CreatedAt,
            item.UpdatedAt,
            Provenance(item.WriteProvenance));

    private static string Provenance(WriteProvenance value)
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private sealed record MemoryJourneyResponse(
        [property: JsonPropertyName("current")] IReadOnlyList<MemoryJourneyItemResponse> Current,
        [property: JsonPropertyName("replaced")] IReadOnlyList<MemoryJourneyItemResponse> Replaced,
        [property: JsonPropertyName("forgotten")] IReadOnlyList<MemoryJourneyItemResponse> Forgotten);

    private sealed record MemoryJourneyItemResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("write_provenance")] string WriteProvenance,
        [property: JsonPropertyName("pinned")] bool Pinned,
        [property: JsonPropertyName("index_terms")] string? IndexTerms,
        [property: JsonPropertyName("superseded_by")] Guid? SupersededBy);

    private sealed record MemoryListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<MemoryExportItem> Items,
        [property: JsonPropertyName("skip")] int Skip,
        [property: JsonPropertyName("take")] int Take);

    private sealed record SetPinnedRequest(
        [property: JsonPropertyName("pinned")] bool? Pinned);

    private sealed record UpsertPreferenceRequest(
        [property: JsonPropertyName("value")] string? Value);

    private sealed record MemoryExportResponse(
        [property: JsonPropertyName("exported_at")] DateTimeOffset ExportedAt,
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("memories")] IReadOnlyList<MemoryExportItem> Memories,
        [property: JsonPropertyName("preferences")] IReadOnlyList<PreferenceExportItem> Preferences);

    private sealed record MemoryExportItem(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("source_conversation_id")] Guid? SourceConversationId,
        [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("write_provenance")] string WriteProvenance,
        [property: JsonPropertyName("pinned")] bool Pinned,
        [property: JsonPropertyName("index_terms")] string? IndexTerms,
        [property: JsonPropertyName("derived_from")] IReadOnlyList<Guid>? DerivedFrom);

    private sealed record PreferenceExportItem(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("write_provenance")] string WriteProvenance);
}
