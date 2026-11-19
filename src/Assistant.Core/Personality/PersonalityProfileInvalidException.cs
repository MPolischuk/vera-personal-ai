namespace Assistant.Core.Personality;

public sealed class PersonalityProfileInvalidException : Exception
{
    public PersonalityProfileInvalidException(string origin, IReadOnlyList<string> problems)
        : base($"The personality profile from '{origin}' is invalid:{Environment.NewLine}" +
               string.Join(Environment.NewLine, problems.Select(p => $"  - {p}")))
        => Problems = problems;

    public IReadOnlyList<string> Problems { get; }
}
