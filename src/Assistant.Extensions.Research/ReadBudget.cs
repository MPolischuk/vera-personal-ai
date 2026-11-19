using System.Collections.Concurrent;
using Assistant.Abstractions.Research;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Research;

/// <summary>
/// Turn budget for <c>read_source</c> only. The key is that tool, not the search counter.
/// <see cref="RecordUseAsync"/> does not write <c>research_searches</c>.
/// </summary>
internal sealed class ReadBudget(IOptions<ResearchOptions> options) : IMeteredToolBudget
{
    public const string ToolName = "read_source";

    private readonly ConcurrentDictionary<Guid, int> _turns = new();
    private readonly int _readsPerTurn = options.Value.ReadsPerTurn;

    public ValueTask<bool> TryConsumeAsync(Guid userId, Guid turnId, CancellationToken cancellationToken)
    {
        _ = userId;
        while (true)
        {
            var current = _turns.GetOrAdd(turnId, 0);
            if (current >= _readsPerTurn)
            {
                return ValueTask.FromResult(false);
            }

            if (_turns.TryUpdate(turnId, current + 1, current))
            {
                return ValueTask.FromResult(true);
            }
        }
    }

    public ValueTask RecordUseAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = userId;
        return ValueTask.CompletedTask;
    }
}
