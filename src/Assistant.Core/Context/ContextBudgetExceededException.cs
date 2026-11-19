namespace Assistant.Core.Context;

public sealed class ContextBudgetExceededException : Exception
{
    public ContextBudgetExceededException(string fragmentName, int requiredTokens, int tokenBudget)
        : base(
            $"Mandatory fragment '{fragmentName}' needs {requiredTokens} tokens " +
            $"and the budget is {tokenBudget}.")
    {
        FragmentName = fragmentName;
        RequiredTokens = requiredTokens;
        TokenBudget = tokenBudget;
    }

    public string FragmentName { get; }
    public int RequiredTokens { get; }
    public int TokenBudget { get; }
}
