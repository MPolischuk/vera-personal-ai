using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Skills;

internal static partial class SkillLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Omitting skill '{SkillName}': {Reason}.")]
    public static partial void OmittingSkill(ILogger logger, string skillName, string reason);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Omitting skill '{SkillName}': the file could not be read.")]
    public static partial void OmittingUnreadableSkill(ILogger logger, Exception exception, string skillName);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Omitting duplicate skill key '{SkillKey}'.")]
    public static partial void OmittingDuplicateKey(ILogger logger, string skillKey);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Omitting prompt '{PromptKey}': the name is invalid or the key is duplicated.")]
    public static partial void OmittingPrompt(ILogger logger, string promptKey);
}
