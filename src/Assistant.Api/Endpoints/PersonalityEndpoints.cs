using Assistant.Core.Personality;

namespace Assistant.Api.Endpoints;

internal static class PersonalityEndpoints
{
    public static IEndpointRouteBuilder MapPersonalityEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/personality", GetAsync);
        return group;
    }

    private static async Task<IResult> GetAsync(IPersonalityService service, CancellationToken ct)
    {
        var active = await service.GetActiveAsync(ct);
        return Results.Ok(new PersonalityResponse(
            ToResponse(active.Profile),
            active.SystemPrompt,
            active.EstimatedTokens));
    }

    private static PersonalityProfileResponse ToResponse(PersonalityProfile profile)
        => new(
            profile.Name,
            profile.Language,
            profile.TimeZone,
            profile.Relationship,
            new PersonalityStyleResponse(
                profile.Style.Formality.ToString(),
                profile.Style.Humor.ToString(),
                profile.Style.Verbosity.ToString(),
                profile.Style.Emoji.ToString()),
            profile.Traits,
            profile.Boundaries,
            profile.Instructions,
            new VoiceSettingsResponse(
                profile.Voice.Provider,
                profile.Voice.VoiceId,
                profile.Voice.Speed),
            new AvatarSettingsResponse(
                profile.Avatar.Model,
                profile.Avatar.IdleAnimation));

    private sealed record PersonalityResponse(
        PersonalityProfileResponse Profile,
        string SystemPrompt,
        int EstimatedTokens);

    private sealed record PersonalityProfileResponse(
        string Name,
        string Language,
        string TimeZone,
        string? Relationship,
        PersonalityStyleResponse Style,
        IReadOnlyList<string> Traits,
        IReadOnlyList<string> Boundaries,
        string? Instructions,
        VoiceSettingsResponse Voice,
        AvatarSettingsResponse Avatar);

    private sealed record PersonalityStyleResponse(
        string Formality,
        string Humor,
        string Verbosity,
        string Emoji);

    private sealed record VoiceSettingsResponse(string Provider, string VoiceId, double Speed);

    private sealed record AvatarSettingsResponse(string Model, string IdleAnimation);
}
