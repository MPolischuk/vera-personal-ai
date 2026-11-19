using Assistant.Abstractions.Scheduling;
using Assistant.Core;
using Assistant.Extensions.Reminders.Firing;
using Assistant.Extensions.Reminders.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Extensions.Reminders;

public static class RemindersServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reminder tools. The host binds <see cref="RemindersOptions"/> and
    /// registers <c>IReminderStore</c> and <c>IReminderQueryService</c>.
    /// </summary>
    public static IServiceCollection AddAssistantReminders(this IServiceCollection services)
    {
        services.AddOptions<RemindersOptions>();
        services.AddScoped<Time.ReminderTimeResolver>();
        services.AddAssistantTool<CreateReminderTool>();
        services.AddAssistantTool<ListRemindersTool>();
        services.AddAssistantTool<CancelReminderTool>();
        services.AddScoped<IJobHandler, ReminderFireHandler>();
        services.AddScoped<IJobHandler, ReminderSnoozeHandler>();
        return services;
    }
}
