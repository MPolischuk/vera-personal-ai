namespace Assistant.Abstractions.Research;

/// <summary>
/// Turn and calendar-month budget for a metered tool.
/// <see cref="TryConsumeAsync"/> runs before the call. <see cref="RecordUseAsync"/> runs only after a usable response.
/// </summary>
public interface IMeteredToolBudget
{
    public const string ExhaustedMessage = "se agotó el cupo de búsqueda";

    public ValueTask<bool> TryConsumeAsync(Guid userId, Guid turnId, CancellationToken cancellationToken);

    public ValueTask RecordUseAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Rows of <c>research_searches</c>. The row is the user and the time, never a title or a snippet.</summary>
public interface IResearchSearchLog
{
    public ValueTask<int> CountAsync(
        Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    public ValueTask AppendAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Admits a metered call, or returns the fixed quota text. A missing budget fails closed.</summary>
public static class MeteredToolBudget
{
    public static async ValueTask<string?> TryAdmitAsync(
        bool metered,
        IMeteredToolBudget? budget,
        Guid userId,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        if (!metered)
        {
            return null;
        }

        if (budget is null
            || !await budget.TryConsumeAsync(userId, turnId, cancellationToken).ConfigureAwait(false))
        {
            return IMeteredToolBudget.ExhaustedMessage;
        }

        return null;
    }
}
