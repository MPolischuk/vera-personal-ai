using Assistant.Abstractions.Tasks;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Providers.Llm;

/// <summary>Prices tokens with the existing <see cref="CostCalculator"/>.</summary>
internal sealed class ModelCostQuote(IOptions<CostOptions> costs) : IModelCostQuote
{
    public decimal Quote(string model, int inputTokens, int outputTokens)
    {
        ArgumentNullException.ThrowIfNull(model);
        var price = CostCalculator.ResolvePrice(costs.Value, model, model);
        return CostCalculator.Calculate(inputTokens, outputTokens, price);
    }
}
