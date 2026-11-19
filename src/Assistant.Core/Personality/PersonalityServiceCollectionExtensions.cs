using Assistant.Core.Context.Contributors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Assistant.Core.Personality;

public static class PersonalityServiceCollectionExtensions
{
    /// <summary>
    /// Does not register <see cref="IPersonalityProfileProvider"/>: where the definition
    /// comes from is decided by the composition root, the only place that knows about a file (D2).
    /// </summary>
    public static IServiceCollection AddAssistantPersonality(this IServiceCollection services)
    {
        services.TryAddSingleton<IPersonalityService, PersonalityService>();

        services.AddContextContributor<PersonalityContributor>();
        services.AddContextContributor<TemporalContributor>();
        services.AddContextContributor<SituationalCharacterContributor>();
        services.AddContextContributor<CharacterCueContributor>();

        return services;
    }
}
