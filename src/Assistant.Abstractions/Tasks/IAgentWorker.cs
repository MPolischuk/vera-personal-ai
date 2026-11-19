namespace Assistant.Abstractions.Tasks;

/// <summary>One kind of background work. The shell calls it at most once per claim.</summary>
public interface IAgentWorker
{
    /// <summary>Matches <c>agent_tasks.kind</c>.</summary>
    public string Kind { get; }

    /// <summary>
    /// Runs the claimed task. Tokens, when present, are priced by the shell.
    /// </summary>
    public ValueTask<AgentWorkerResult> ExecuteAsync(ClaimedAgentTask task, CancellationToken ct);
}

/// <summary>Outcome of one worker call. Null tokens leave the row cost unchanged.</summary>
public sealed record AgentWorkerResult(int? InputTokens = null, int? OutputTokens = null, string? Model = null);
