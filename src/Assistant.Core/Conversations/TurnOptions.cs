using System.ComponentModel.DataAnnotations;

namespace Assistant.Core.Conversations;

public sealed class TurnOptions
{
    public const string SectionName = "Assistant:Turn";

    /// <summary>Function-calling rounds before cutting the loop (D4).</summary>
    [Range(1, 10)]
    public int MaxToolIterations { get; set; } = 5;
}
