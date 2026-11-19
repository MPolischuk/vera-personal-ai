using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Abstractions.Events;
using Assistant.Abstractions.Tools;
using Assistant.Core.Context;
using Assistant.Core.Events;
using Assistant.Core.Permissions;
using Assistant.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Assistant.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITokenEstimator, HeuristicTokenEstimator>();

        services.AddScoped<IContextAssembler, ContextAssembler>();
        services.AddScoped<IContextBudgetPolicy, PriorityWithHistoryTrimPolicy>();

        services.AddScoped<IPermissionPolicy, DefaultPermissionPolicy>();
        services.AddScoped<IPermissionEngine, PermissionEngine>();

        // D15: scoped, because tools may depend on scoped services.
        services.AddScoped<ToolCatalog>();
        services.AddScoped<IToolRegistry, ToolRegistry>();
        services.AddScoped<IToolDispatcher, ToolDispatcher>();
        services.AddScoped<ToolDeclarationFilter>();

        services.AddSingleton<ChannelEventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IDomainEventPump>(sp => sp.GetRequiredService<ChannelEventBus>());

        return services;
    }

    /// <summary>
    /// Registering a tool is this and nothing more. That this line is the only change
    /// when adding a capability is what PT-10 verifies with git diff (criterion B2).
    /// </summary>
    public static IServiceCollection AddAssistantTool<TTool>(this IServiceCollection services)
        where TTool : class, ITool
        => services.AddScoped<ITool, TTool>();

    public static IServiceCollection AddContextContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IContextContributor
        => services.AddScoped<IContextContributor, TContributor>();

    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
        => services.AddScoped<IDomainEventHandler<TEvent>, THandler>();
}
