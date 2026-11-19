using Assistant.Abstractions;
using Assistant.Abstractions.Context;

namespace Assistant.Core.Context;

internal sealed class PriorityWithHistoryTrimPolicy(ITokenEstimator estimator) : IContextBudgetPolicy
{
    public IReadOnlyList<ContextFragment> Fit(IReadOnlyList<ContextFragment> fragments, int tokenBudget)
    {
        var accepted = new List<ContextFragment>(fragments.Count);
        var remaining = tokenBudget;

        // Ascending priority; equal priority by name so the result does not depend
        // on registration order in the container.
        foreach (var fragment in fragments
                     .OrderBy(f => f.Priority)
                     .ThenBy(f => f.Name, StringComparer.Ordinal))
        {
            var cost = Cost(fragment);

            if (cost <= remaining)
            {
                accepted.Add(fragment);
                remaining -= cost;
                continue;
            }

            if (fragment.Trim is FragmentTrim.FromStart
                && Trim(fragment, remaining) is { } trimmed)
            {
                CoreTelemetry.PartsTrimmed.Add(
                    fragment.Parts.Count - trimmed.Parts.Count,
                    new KeyValuePair<string, object?>("fragment", fragment.Name));

                accepted.Add(trimmed);
                remaining -= Cost(trimmed);
                continue;
            }

            if (fragment.Priority == ContextPriority.Mandatory)
            {
                throw new ContextBudgetExceededException(fragment.Name, cost, tokenBudget);
            }

            CoreTelemetry.FragmentsDropped.Add(
                1, new KeyValuePair<string, object?>("fragment", fragment.Name));
        }

        return accepted;
    }

    private int Cost(ContextFragment fragment)
        => fragment.EstimatedTokens > 0
            ? fragment.EstimatedTokens
            : estimator.Estimate(fragment.Content);

    /// <summary>Drops parts from the old end until it fits. Null if not even one fits.</summary>
    private ContextFragment? Trim(ContextFragment fragment, int available)
    {
        if (available <= 0 || fragment.Parts.Count == 0)
        {
            return null;
        }

        var parts = fragment.Parts.ToList();

        while (parts.Count > 0)
        {
            var content = string.Join("\n", parts);
            var cost = estimator.Estimate(content);

            if (cost <= available)
            {
                return fragment with
                {
                    Parts = parts,
                    EstimatedTokens = cost,
                };
            }

            parts.RemoveAt(0);
        }

        return null;
    }
}
