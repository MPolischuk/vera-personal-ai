namespace Assistant.Abstractions.Tasks;

/// <summary>Wire values of <c>agent_tasks.state</c>. <c>canceled</c> has one L.</summary>
public static class AgentTaskStatuses
{
    public const string Submitted = "submitted";

    public const string Working = "working";

    public const string InputRequired = "input-required";

    public const string AuthRequired = "auth-required";

    public const string Completed = "completed";

    public const string Failed = "failed";

    public const string Canceled = "canceled";

    public const string Rejected = "rejected";

    public static bool IsTerminal(string state)
        => state is Completed or Failed or Canceled or Rejected;

    public static bool IsCancellable(string state)
        => state is Submitted or Working or InputRequired;
}

/// <summary>Wire values of <c>agent_tasks.failure_reason</c>. Separate from the state.</summary>
public static class AgentTaskFailureReasons
{
    public const string Interrupted = "interrupted";

    public const string ProviderBudget = "provider_budget";

    public const string Network = "network";

    public const string CostCap = "cost_cap";

    public const string Deadline = "deadline";

    public const string UnknownKind = "unknown_kind";

    public const string WorkerError = "worker_error";
}
