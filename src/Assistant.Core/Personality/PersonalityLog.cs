using System.Threading;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Personality;

internal static partial class PersonalityLog
{
    private static int _invalidCueLogged;

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "Active personality profile: {Name} ({Tokens} estimated tokens)")]
    public static partial void ActiveProfile(ILogger logger, string name, int tokens);

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Information,
        Message = "Situational character ids: {Ids}")]
    public static partial void SituationalIds(ILogger logger, string ids);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Warning,
        Message = "Invalid character cue preamble stripped")]
    private static partial void InvalidCue(ILogger logger);

    [LoggerMessage(
        EventId = 103,
        Level = LogLevel.Information,
        Message = "Character cue {Outcome}; model start: {Preview}")]
    public static partial void CueOutcome(ILogger logger, string outcome, string preview);

    public static void LogInvalidCueOnce(ILogger logger)
    {
        if (Interlocked.Exchange(ref _invalidCueLogged, 1) != 0)
        {
            return;
        }

        InvalidCue(logger);
    }
}
