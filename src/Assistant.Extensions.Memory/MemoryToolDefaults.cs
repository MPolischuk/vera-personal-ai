namespace Assistant.Extensions.Memory;

internal static class MemoryToolDefaults
{
    /// <summary>
    /// Cosine floor for the remember duplicate check. Measured 2026-11-05 on
    /// Harrier-OSS-v1 270M: paraphrases 0.902–0.936, distinct facts 0.721–0.780.
    /// </summary>
    public const float DuplicateMinScore = 0.84f;

    public const int DuplicateTopK = 1;

    public const int RecallTopK = 5;

    /// <summary>
    /// Cosine floor for search. Measured on the V2-05.1 golden set: true hits
    /// 0.620–0.771, abstention top 0.582. This floor keeps those hits.
    /// </summary>
    public const float RecallMinScore = 0.59f;

    public const string UnspecifiedEmbeddingModel = "unspecified";

    public const int MaxPinned = 8;

    public const int MaxSearchQueries = 3;
}
