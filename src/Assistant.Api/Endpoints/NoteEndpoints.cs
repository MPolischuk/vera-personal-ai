using System.Security.Claims;
using Assistant.Api.Authentication;
using Assistant.Core.Notes;
using Assistant.Extensions.Notes;

namespace Assistant.Api.Endpoints;

internal static class NoteEndpoints
{
    public static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/notes/export", ExportAsync);
        return group;
    }

    private static async Task<IResult> ExportAsync(
        ClaimsPrincipal user,
        INoteStore notes,
        CancellationToken ct)
    {
        var archive = NoteArchive.Build(await notes.ListActiveAsync(user.UserId(), ct));
        return Results.File(archive, "application/zip", "notes.zip");
    }
}
