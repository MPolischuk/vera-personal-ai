using Assistant.Core.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Core.Audit;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantAudit(this IServiceCollection services)
    {
        services.AddDomainEventHandler<TurnStarted, AuditLogSubscriber>();
        services.AddDomainEventHandler<TurnCompleted, AuditLogSubscriber>();
        services.AddDomainEventHandler<TurnInterrupted, AuditLogSubscriber>();
        services.AddDomainEventHandler<ToolInvoked, AuditLogSubscriber>();
        services.AddDomainEventHandler<ConfirmationRequested, AuditLogSubscriber>();
        services.AddDomainEventHandler<ConfirmationResolved, AuditLogSubscriber>();

        return services;
    }
}
