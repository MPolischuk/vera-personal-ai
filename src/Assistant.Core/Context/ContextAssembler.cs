using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Core.Configuration;
using Assistant.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Context;

internal sealed class ContextAssembler(
    IEnumerable<IContextContributor> contributors,
    IContextBudgetPolicy policy,
    ITokenEstimator estimator,
    IOptions<ContextOptions> options,
    ILogger<ContextAssembler> log) : IContextAssembler
{
    public async ValueTask<AssembledContext> AssembleAsync(ContextRequest request, CancellationToken ct)
    {
        var fragments = new List<ContextFragment>();

        // Sequential on purpose: with three contributors parallelism buys nothing
        // and deterministic order simplifies tests. Parallelize here later if V7
        // measurements ask for it; it does not change the IContextContributor contract.
        foreach (var contributor in contributors.Where(c => c.IsEnabled))
        {
            ContextFragment? fragment;

            try
            {
                fragment = await contributor.ContributeAsync(request, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException
                                   && contributor.Priority != ContextPriority.Mandatory)
            {
                // A third-party contributor that blows up must not take down the turn.
                // A mandatory one must: without identity the reply would be another assistant's.
                CoreLog.ContributorFailed(log, e, contributor.Name);
                continue;
            }

            if (fragment is null)
            {
                continue;
            }

            fragments.Add(fragment with
            {
                Priority = contributor.Priority,
                EstimatedTokens = fragment.EstimatedTokens > 0
                    ? fragment.EstimatedTokens
                    : estimator.Estimate(fragment.Content),
            });
        }

        var accepted = policy.Fit(fragments, options.Value.TokenBudget);
        var contaminated = accepted.Any(f => UntrustedContentEnvelope.ContainsMarker(f.Content))
            || UntrustedContentEnvelope.ContainsMarker(request.UserMessage);

        return new AssembledContext(accepted, accepted.Sum(f => f.EstimatedTokens), contaminated);
    }
}
