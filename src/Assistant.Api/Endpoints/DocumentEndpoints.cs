using System.Security.Claims;
using Assistant.Api.Authentication;
using Assistant.Core.Research;
using Assistant.Extensions.Research;

namespace Assistant.Api.Endpoints;

internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapPost("/documents", UploadAsync);
        return group;
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IDocumentLibrary library,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.Problem(
                title: "The document must be uploaded as multipart form data.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                title: "The file is missing.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var mediaType = DocumentLibrary.ResolveMediaType(file.FileName, file.ContentType);
        if (mediaType is null)
        {
            return Results.Problem(
                title: "The document type is not supported.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (file.Length > DocumentLibrary.DefaultMaxBytes)
        {
            return Results.Problem(
                title: "The document is larger than 15 MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var id = await library.SaveAsync(user.UserId(), stream, mediaType, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new { id });
        }
        catch (DocumentTooLargeException)
        {
            return Results.Problem(
                title: "The document is larger than 15 MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        catch (ArgumentException)
        {
            return Results.Problem(
                title: "The file is missing.",
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
