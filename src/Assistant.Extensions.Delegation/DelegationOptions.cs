namespace Assistant.Extensions.Delegation;

public sealed class DelegationOptions
{
    public const string SectionName = "Delegation";

    public bool Enabled { get; set; } = true;

    public decimal CostCapUsd { get; set; } = 0.10m;

    public TimeSpan TimeCap { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan RenewEvery { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxConcurrent { get; set; } = 1;

    public int PartialFlushCharacters { get; set; } = 500;

    public int InstructionMaxCharacters { get; set; } = 8000;

    public int TitleMaxCharacters { get; set; } = 60;

    public string[] EgressHosts { get; set; } = [];

    public string? RemoteEndpoint { get; set; }
}
