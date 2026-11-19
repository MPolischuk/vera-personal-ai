using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Core.Usage;

public static class UsageServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantUsageAccounting(this IServiceCollection services)
        => services.AddDomainEventHandler<UsageRecorded, UsageRecordingSubscriber>();
}
