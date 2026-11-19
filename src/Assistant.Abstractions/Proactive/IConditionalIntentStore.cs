namespace Assistant.Abstractions.Proactive;

/// <summary>The only event this step publishes. Any other name is rejected.</summary>
public static class ConditionalIntentEvents
{
    public const string InboundFact = "inbound.fact";

    public static bool IsAllowed(string? eventName)
        => string.Equals(eventName, InboundFact, StringComparison.Ordinal);
}

/// <summary>Wire values for <c>conditional_intents.status</c>.</summary>
public static class ConditionalIntentStatuses
{
    public const string Active = "active";

    public const string Paused = "paused";
}

/// <summary>Limits for a conditional intent.</summary>
public static class ConditionalIntentLimits
{
    public const int TextMaxLength = 300;
}

/// <summary>Text to publish when <see cref="EventName"/> arrives. It is not a reminder.</summary>
public sealed record ConditionalIntent(
    Guid Id,
    Guid UserId,
    string EventName,
    string Text,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Conditional intents. Only <see cref="ConditionalIntentEvents.InboundFact"/> can be stored.</summary>
public interface IConditionalIntentStore
{
    public ValueTask<ConditionalIntent> InsertAsync(
        Guid userId, string eventName, string text, DateTimeOffset now, CancellationToken ct);

    /// <summary>False when the row is missing, belongs to someone else, or is already paused.</summary>
    public ValueTask<bool> PauseAsync(Guid userId, Guid intentId, CancellationToken ct);

    public ValueTask<IReadOnlyList<ConditionalIntent>> ListAsync(Guid userId, CancellationToken ct);
}
