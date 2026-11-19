using Assistant.Abstractions.Tools;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Tools;

internal static partial class ToolCatalogLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Omitting tool '{ToolName}' from extension '{ExtensionName}': name is not snake_case.")]
    public static partial void OmittingInvalidName(ILogger logger, string toolName, string extensionName);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Omitting tool '{ToolName}' from extension '{ExtensionName}': name already claimed by '{ExistingExtension}'.")]
    public static partial void OmittingDuplicateName(
        ILogger logger, string toolName, string extensionName, string existingExtension);
}
