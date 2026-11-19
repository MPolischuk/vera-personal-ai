using Assistant.Abstractions.Context;

namespace Assistant.Core.Context;

public interface IContextBudgetPolicy
{
    public IReadOnlyList<ContextFragment> Fit(IReadOnlyList<ContextFragment> fragments, int tokenBudget);
}
