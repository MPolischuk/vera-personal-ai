using System.Collections.Concurrent;
using Assistant.Abstractions.Research;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Extensions.Research;

/// <summary>
/// Counts searches in memory per turn and in <c>research_searches</c> per UTC calendar month.
/// </summary>
public sealed class SearchBudget(IServiceScopeFactory scopes, IOptions<ResearchOptions> options) : IMeteredToolBudget
{
    private readonly ConcurrentDictionary<Guid, int> _turns = new();
    private readonly ResearchOptions _options = options.Value;

    public async ValueTask<bool> TryConsumeAsync(Guid userId, Guid turnId, CancellationToken cancellationToken)
    {
        if (TurnCount(turnId) >= _options.SearchesPerTurn)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<IResearchSearchLog>();
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var used = await log.CountAsync(userId, start, start.AddMonths(1), cancellationToken).ConfigureAwait(false);
        if (used >= _options.SearchesPerMonth)
        {
            return false;
        }

        return TryIncrementTurn(turnId);
    }

    public async ValueTask RecordUseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<IResearchSearchLog>();
        await log.AppendAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private int TurnCount(Guid turnId)
        => _turns.TryGetValue(turnId, out var count) ? count : 0;

    private bool TryIncrementTurn(Guid turnId)
    {
        while (true)
        {
            var current = _turns.GetOrAdd(turnId, 0);
            if (current >= _options.SearchesPerTurn)
            {
                return false;
            }

            if (_turns.TryUpdate(turnId, current + 1, current))
            {
                return true;
            }
        }
    }
}
