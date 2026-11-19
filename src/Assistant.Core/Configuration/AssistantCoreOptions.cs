using System.ComponentModel.DataAnnotations;

namespace Assistant.Core.Configuration;

public sealed class ContextOptions
{
    public const string SectionName = "Assistant:Context";

    [Range(1_000, 200_000)]
    public int TokenBudget { get; set; } = 8_000;

    [Range(1, 500)]
    public int HistoryMaxMessages { get; set; } = 40;
}

public sealed class PermissionOptions
{
    public const string SectionName = "Assistant:Permissions";

    public bool AutoAllowReversible { get; set; } = true;

    [Range(10, 3_600)]
    public int ConfirmationTimeoutSeconds { get; set; } = 300;
}

public sealed class ToolOptions
{
    public const string SectionName = "Assistant:Tools";

    /// <summary>Global invocation timeout. Per-tool when some tool asks for it.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
