namespace Assistant.Abstractions.Proactive;

/// <summary>Wire values for <c>standing_orders.status</c>.</summary>
public static class StandingOrderStatuses
{
    public const string Active = "active";

    public const string Paused = "paused";
}

/// <summary>Limits for a standing order. The prompt keeps at most <see cref="PromptLines"/> active lines.</summary>
public static class StandingOrderLimits
{
    public const int TextMaxLength = 500;

    public const int PromptLines = 10;
}

/// <summary>An order that stays in force until it is paused. Chat text never becomes a row.</summary>
public sealed record StandingOrder(
    Guid Id,
    Guid UserId,
    string Text,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Standing orders. The turn assembler does not read this store.</summary>
public interface IStandingOrderStore
{
    public ValueTask<StandingOrder> InsertAsync(Guid userId, string text, DateTimeOffset now, CancellationToken ct);

    /// <summary>False when the row is missing, belongs to someone else, or is already paused.</summary>
    public ValueTask<bool> PauseAsync(Guid userId, Guid orderId, CancellationToken ct);

    public ValueTask<IReadOnlyList<StandingOrder>> ListAsync(Guid userId, CancellationToken ct);
}
