using Assistant.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Skills;

public static class SkillServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantSkills(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<SkillOptions>()
            .Bind(configuration.GetSection(SkillOptions.SectionName));
        services.AddSingleton<SkillCatalog>();
        services.AddSingleton<SkillFileWriter>();
        services.AddAssistantTool<LoadSkillTool>();
        services.AddAssistantTool<ProposeSkillTool>();
        return services;
    }
}
