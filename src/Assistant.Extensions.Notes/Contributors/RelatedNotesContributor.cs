using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Notes;
using Assistant.Core.Tools;
using Assistant.Extensions.Notes.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Notes.Contributors;

internal sealed class RelatedNotesContributor(
    IMemoryEmbedder embeddings,
    INoteStore notes,
    ITokenEstimator estimator,
    ILogger<RelatedNotesContributor> logger) : IContextContributor
{
    public string Name => NoteDefaults.RelatedContributorName;

    public int Priority => NoteDefaults.RelatedPriority;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return null;
        }

        Embedding<float> embedding;
        try
        {
            var generated = await embeddings.EmbedQueryAsync(
                [request.UserMessage.Trim()],
                EmbeddingCallOptions.For(new ModelCallContext(request.ConversationId, request.TurnId)),
                ct);
            embedding = generated[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NoteLog.EmbeddingFailed(logger, ex, request.TurnId);
            return null;
        }

        var hits = await notes.SearchAsync(
            new SearchNotesQuery(request.UserId, embedding.Vector.ToArray(), NoteDefaults.SearchTopK),
            ct);
        if (hits.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var hit in hits)
        {
            var claim = hit.Note.Sources.Count == 0 ? null : hit.Note.Sources[0].ClaimText;
            var body = string.IsNullOrWhiteSpace(claim)
                ? hit.Note.Title
                : hit.Note.Title + "\n" + claim;
            var wrapped = ExternalContent.Wrap("notes", "note", hit.Note.Title, body);
            var candidate = parts.Count == 0 ? wrapped : string.Join('\n', parts) + "\n" + wrapped;
            if (estimator.Estimate(candidate) > NoteDefaults.RelatedTokenBudget)
            {
                break;
            }

            parts.Add(wrapped);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var content = string.Join('\n', parts);
        return ContextFragment.Text(Name, ContextPlacement.AppendedMessage, content, estimator.Estimate(content));
    }
}
