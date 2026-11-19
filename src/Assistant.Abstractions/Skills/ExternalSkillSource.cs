namespace Assistant.Abstractions.Skills;

public sealed record ExternalSkillSummary(string Key, string Description, string Origin);

public interface IExternalSkillSource
{
    public IReadOnlyList<ExternalSkillSummary> List();

    public ValueTask<string?> LoadTextAsync(string key, CancellationToken ct);
}
