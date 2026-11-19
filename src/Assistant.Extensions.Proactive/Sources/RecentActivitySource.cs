using System.Globalization;
using Assistant.Abstractions.Proactive;
using Assistant.Abstractions.Tasks;

namespace Assistant.Extensions.Proactive.Sources;

/// <summary>
/// The latest five tasks as kind, state and date.
/// Uses the list that already omits the instruction and the result.
/// </summary>
public sealed class RecentActivitySource(IAgentTaskStore tasks) : IAmbientSource
{
    public const int RowLimit = 5;

    public string Name => "recent-activity";

    public async ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var rows = await tasks.ListForUserAsync(userId, ct);
        var lines = rows
            .OrderByDescending(row => row.CreatedAt)
            .Take(RowLimit)
            .Select(row => FormattableString.Invariant($"{row.Kind} {row.State} {row.CreatedAt:O}"))
            .ToArray();
        return lines.Length == 0 ? null : string.Join('\n', lines);
    }
}
