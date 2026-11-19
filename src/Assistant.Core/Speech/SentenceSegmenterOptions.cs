using System.ComponentModel.DataAnnotations;

namespace Assistant.Core.Speech;

public sealed class SentenceSegmenterOptions
{
    public const string SectionName = "Assistant:Speech:Segmenter";

    [Range(1, 200)]
    public int FirstMinChars { get; set; } = 1;

    [Range(1, 500)]
    public int SubsequentMinChars { get; set; } = 48;

    [Range(8, 500)]
    public int FirstMaxChars { get; set; } = 80;

    [Range(8, 2_000)]
    public int SubsequentMaxChars { get; set; } = 220;
}
