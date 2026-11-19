using Assistant.Api.Webhooks;
using Assistant.Core.Configuration;
using Assistant.Extensions.Reminders;
using Assistant.Core.Conversations;
using Assistant.Core.Personality;
using Assistant.Core.Speech;
using Assistant.Providers.Llm.Configuration;
using Assistant.Providers.Speech.Configuration;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Configuration;

internal static class OptionsRegistration
{
    public static IServiceCollection AddAssistantOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        Register<ContextOptions>(services, configuration, ContextOptions.SectionName);
        Register<PermissionOptions>(services, configuration, PermissionOptions.SectionName);
        Register<ToolOptions>(services, configuration, ToolOptions.SectionName);
        Register<TurnOptions>(services, configuration, TurnOptions.SectionName);
        Register<PersonalityOptions>(services, configuration, PersonalityOptions.SectionName);
        Register<ModelsOptions>(services, configuration, ModelsOptions.SectionName);
        Register<CostOptions>(services, configuration, CostOptions.SectionName);
        Register<SpeechOptions>(services, configuration, SpeechOptions.SectionName);
        Register<SentenceSegmenterOptions>(services, configuration, SentenceSegmenterOptions.SectionName);
        RegisterAuth(services, configuration);
        Register<ApiOptions>(services, configuration, ApiOptions.SectionName);
        RegisterReminders(services, configuration);
        RegisterInboundWebhooks(services, configuration);

        return services;
    }

    private static void RegisterAuth(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>>(new AuthOptionsValidator(configuration));
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .PostConfigure(options =>
            {
                if (AuthModes.TryParse(configuration[$"{AuthOptions.SectionName}:Mode"], out var mode))
                {
                    options.Mode = mode;
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void RegisterReminders(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<RemindersOptions>, RemindersOptionsValidator>();
        services.AddOptions<RemindersOptions>()
            .Bind(configuration.GetSection(RemindersOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void RegisterInboundWebhooks(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<InboundWebhookOptions>, InboundWebhookOptionsValidator>();
        services.AddOptions<InboundWebhookOptions>()
            .Bind(configuration.GetSection(InboundWebhookOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void Register<TOptions>(
        IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
        => services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
