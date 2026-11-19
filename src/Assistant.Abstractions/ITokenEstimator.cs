namespace Assistant.Abstractions;

/// <summary>
/// Shared token estimation. Lives in the SDK because the contributor declares
/// <see cref="Context.ContextFragment.EstimatedTokens"/>, and if each estimates
/// differently the budget stops being comparable (D5).
/// </summary>
public interface ITokenEstimator
{
    public int Estimate(string text);
}
