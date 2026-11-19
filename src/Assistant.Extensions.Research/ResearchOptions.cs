namespace Assistant.Extensions.Research;

/// <summary>Budgets for search and page reads. Bound from the <c>Research</c> section.</summary>
public sealed class ResearchOptions
{
    public const string SectionName = "Research";

    public int SearchesPerTurn { get; set; } = 4;

    public int ReadsPerTurn { get; set; } = 3;

    public int SearchesPerMonth { get; set; } = 1000;

    public int MaxPageBytes { get; set; } = 1_000_000;

    public int MaxRedirects { get; set; } = 3;

    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan PageTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan ConversionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public int MaxDocumentBytes { get; set; } = 15 * 1024 * 1024;

    /// <summary>Directory of the <c>document-data</c> volume. One folder per user.</summary>
    public string DocumentRoot { get; set; } = "/var/lib/assistant/documents";

    /// <summary>Base URL of docling-serve on the internal network. Null disables conversion.</summary>
    public Uri? DoclingBaseUrl { get; set; }

    /// <summary>
    /// Sent as <c>X-Api-Key</c> when docling-serve requires one. Empty leaves the header off.
    /// </summary>
    public string? DoclingApiKey { get; set; }
}
