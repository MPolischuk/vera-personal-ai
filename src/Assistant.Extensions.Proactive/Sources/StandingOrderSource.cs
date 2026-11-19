using Assistant.Abstractions.Proactive;

namespace Assistant.Extensions.Proactive.Sources;

/// <summary>Active standing orders, one line each, at most ten. Not a turn contributor.</summary>
public sealed class StandingOrderSource(IStandingOrderStore orders) : IAmbientSource
{
    public string Name => "standing-orders";

    public async ValueTask<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var rows = await orders.ListAsync(userId, ct);
        var lines = rows
            .Where(order => order.Status == StandingOrderStatuses.Active)
            .OrderByDescending(order => order.CreatedAt)
            .Take(StandingOrderLimits.PromptLines)
            .Select(order => OneLine(order.Text))
            .Where(line => line.Length > 0)
            .ToArray();
        return lines.Length == 0 ? null : string.Join('\n', lines);
    }

    private static string OneLine(string text)
        => string.Join(' ', text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
}
