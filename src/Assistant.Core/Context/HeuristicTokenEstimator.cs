using Assistant.Abstractions;

namespace Assistant.Core.Context;

internal sealed class HeuristicTokenEstimator : ITokenEstimator
{
    // Characters divided by four, rounding up. Design allows it because the budget
    // has margin; PT-05 may add a real-tokenizer implementation.
    public int Estimate(string text)
        => string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;
}
