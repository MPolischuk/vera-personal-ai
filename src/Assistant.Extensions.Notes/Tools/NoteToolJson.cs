using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Core.Notes;

namespace Assistant.Extensions.Notes.Tools;

internal static class NoteToolJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Created(Note note)
        => JsonSerializer.Serialize(
            new CreatedResponse(note.Id, note.Title, note.Sources.Count),
            Json);

    public static string Updated(Note note)
        => JsonSerializer.Serialize(new UpdatedResponse(note.Id, note.Title, note.VerifiedAt), Json);

    public static string Linked(Guid noteId, Guid otherNoteId)
        => JsonSerializer.Serialize(new LinkedResponse(noteId, otherNoteId), Json);

    public static string Read(Note note)
        => JsonSerializer.Serialize(
            new ReadResponse(
                note.Id,
                note.Title,
                note.Body,
                note.VerifiedAt,
                note.Sources.Select(ToSource).ToList(),
                note.Neighbors.Select(neighbor => neighbor.Title).ToList()),
            Json);

    public static string Search(IReadOnlyList<ScoredNote> hits)
        => JsonSerializer.Serialize(
            new SearchResponse(hits.Select(hit => new SearchHit(
                hit.Note.Id,
                hit.Note.Title,
                hit.Note.Sources.Count == 0 ? null : hit.Note.Sources[0].ClaimText,
                hit.Score)).ToList()),
            Json);

    private static SourceResponse ToSource(NoteSource source)
        => new(
            source.ClaimText,
            source.Fragment,
            source.RetrievedAt,
            source.Url,
            source.DocumentId,
            source.Page,
            source.Bbox is { } bbox ? [bbox.Left, bbox.Top, bbox.Right, bbox.Bottom] : null);

    private sealed record CreatedResponse(
        [property: JsonPropertyName("note_id")] Guid NoteId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("source_count")] int SourceCount);

    private sealed record UpdatedResponse(
        [property: JsonPropertyName("note_id")] Guid NoteId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("verified_at")] DateTimeOffset? VerifiedAt);

    private sealed record LinkedResponse(
        [property: JsonPropertyName("note_id")] Guid NoteId,
        [property: JsonPropertyName("other_note_id")] Guid OtherNoteId);

    private sealed record ReadResponse(
        [property: JsonPropertyName("note_id")] Guid NoteId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("verified_at")] DateTimeOffset? VerifiedAt,
        [property: JsonPropertyName("sources")] IReadOnlyList<SourceResponse> Sources,
        [property: JsonPropertyName("links")] IReadOnlyList<string> Links);

    private sealed record SourceResponse(
        [property: JsonPropertyName("claim")] string Claim,
        [property: JsonPropertyName("fragment")] string Fragment,
        [property: JsonPropertyName("retrieved_at")] DateTimeOffset RetrievedAt,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("document_id")] Guid? DocumentId,
        [property: JsonPropertyName("page")] int? Page,
        [property: JsonPropertyName("bbox")] double[]? Bbox);

    private sealed record SearchResponse(
        [property: JsonPropertyName("notes")] IReadOnlyList<SearchHit> Notes);

    private sealed record SearchHit(
        [property: JsonPropertyName("note_id")] Guid NoteId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("claim")] string? Claim,
        [property: JsonPropertyName("score")] float Score);
}
