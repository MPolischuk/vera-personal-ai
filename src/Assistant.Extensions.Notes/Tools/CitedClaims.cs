using Assistant.Core.Notes;
using Assistant.Core.Research;

namespace Assistant.Extensions.Notes.Tools;

/// <summary>
/// Indexes point into the claims of the reads concatenated in that order.
/// An empty index list copies every claim. The fragment is never taken from the tool call.
/// </summary>
internal static class CitedClaims
{
    public static bool TrySelect(
        IReadOnlyList<ResearchRead> reads,
        IReadOnlyList<int>? indexes,
        out IReadOnlyList<NoteSource> sources,
        out string error)
    {
        var all = new List<(ResearchRead Read, ResearchClaim Claim)>();
        foreach (var read in reads)
        {
            foreach (var claim in read.Claims)
            {
                all.Add((read, claim));
            }
        }

        List<(ResearchRead Read, ResearchClaim Claim)> selected;
        if (indexes is null || indexes.Count == 0)
        {
            selected = all;
        }
        else
        {
            selected = new List<(ResearchRead, ResearchClaim)>(indexes.Count);
            foreach (var index in indexes)
            {
                if (index < 0 || index >= all.Count)
                {
                    sources = [];
                    error = "El índice de afirmación está fuera de rango.";
                    return false;
                }

                selected.Add(all[index]);
            }
        }

        if (selected.Count == 0)
        {
            sources = [];
            error = "La lectura no tiene afirmaciones para copiar.";
            return false;
        }

        sources = selected.Select(pair => ToSource(pair.Read, pair.Claim)).ToList();
        error = "";
        return true;
    }

    private static NoteSource ToSource(ResearchRead read, ResearchClaim claim)
    {
        if (claim.DocumentId is { } documentId)
        {
            return new NoteSource(
                claim.Text,
                claim.Fragment,
                read.RetrievedAt,
                Url: null,
                documentId,
                claim.Page,
                claim.Bbox);
        }

        return new NoteSource(
            claim.Text,
            claim.Fragment,
            read.RetrievedAt,
            read.SourceUrl,
            DocumentId: null,
            Page: null,
            Bbox: null);
    }
}
