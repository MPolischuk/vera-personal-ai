using Assistant.Abstractions.Scheduling;
using Assistant.Scheduling.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Assistant.Scheduling;

public static class SchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<SchedulerOptions>>(new SchedulerOptionsValidator());
        services.AddOptions<SchedulerOptions>()
            .Bind(configuration.GetSection(SchedulerOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<JobWakeSignal>();
        services.AddScoped<IJobScheduler, JobScheduler>();
        services.AddScoped<JobHandlerRegistry>();
        services.AddJobHandler<SchedulerPurgeHandler>();

        if (IsEnabled(configuration))
        {
            services.AddHostedService<SchedulerWorker>();
        }

        return services;
    }

    public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler
    {
        services.AddScoped<IJobHandler, THandler>();
        return services;
    }

    private static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration[$"{SchedulerOptions.SectionName}:Enabled"];
        return !bool.TryParse(raw, out var enabled) || enabled;
    }
}
