using System.Diagnostics;

namespace Assistant.Extensions.Skills;

internal static class SkillTelemetry
{
    public const string ActivitySourceName = "Assistant.Extensions.Skills";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
