using Assistant.Providers.Llm.Configuration;

namespace Assistant.Providers.Llm;

internal static class CostCalculator
{
    public static decimal Calculate(
        int input,
        int output,
        ModelPriceOptions price,
        int cachedInput = 0,
        int cacheWrite = 0)
    {
        var billableInput = Math.Max(0, input);
        var billableOutput = Math.Max(0, output);
        var cached = Math.Clamp(cachedInput, 0, billableInput);
        var writes = Math.Max(0, cacheWrite);
        var fresh = billableInput - cached;
        var readPrice = price.CachedInputPerMillionUsd ?? price.InputPerMillionUsd;
        var writePrice = price.CacheWritePerMillionUsd ?? price.InputPerMillionUsd;

        var cost = (fresh / 1_000_000m * price.InputPerMillionUsd)
            + (cached / 1_000_000m * readPrice)
            + (writes / 1_000_000m * writePrice)
            + (billableOutput / 1_000_000m * price.OutputPerMillionUsd);
        return decimal.Round(cost, 6, MidpointRounding.AwayFromZero);
    }

    public static ModelPriceOptions ResolvePrice(
        CostOptions costs, string? reportedModel, string configuredModel)
    {
        if (reportedModel is not null
            && costs.Prices.TryGetValue(reportedModel, out var byReported))
        {
            return byReported;
        }

        if (costs.Prices.TryGetValue(configuredModel, out var byConfigured))
        {
            return byConfigured;
        }

        // Startup validation should have prevented this; an explicit zero is the runtime
        // "unknown" exit without crashing the turn.
        return new ModelPriceOptions();
    }
}
