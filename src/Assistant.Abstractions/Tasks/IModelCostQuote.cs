namespace Assistant.Abstractions.Tasks;

/// <summary>
/// Prices one model call from its token counts. Does not write a usage row.
/// </summary>
public interface IModelCostQuote
{
    /// <summary>Returns the cost in USD, rounded to 6 decimal places.</summary>
    public decimal Quote(string model, int inputTokens, int outputTokens);
}
