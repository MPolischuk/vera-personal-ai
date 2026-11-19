using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Llm;

internal static partial class LlmLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "LLM usage recorded: {Provider}/{Model} input={Input} output={Output} cost={Cost} reported={Reported}")]
    public static partial void UsageRecorded(
        ILogger logger, string provider, string model, int input, int output, decimal cost, bool reported);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Provider did not report usage; tokens estimated for {Provider}/{Model}")]
    public static partial void UsageEstimated(ILogger logger, string provider, string model);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Failed to publish LLM usage for {Provider}/{Model}")]
    public static partial void UsagePublishFailed(
        ILogger logger, Exception exception, string provider, string model);
}
