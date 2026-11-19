namespace Assistant.Api.Audit;

internal static class AgenticRiskCatalog
{
    public static IReadOnlyList<AgenticGap> Gaps { get; } =
    [
        new("ASI05", "V2-10"),
        new("ASI06", "V2-08"),
        new("ASI10", "V2-10"),
    ];
}

internal readonly record struct AgenticGap(string Risk, string Package);
