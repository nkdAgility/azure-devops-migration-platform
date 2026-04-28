#if !NET481
using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Extension methods for registering connector-specific factory implementations with the
/// composite dispatchers <see cref="CompositeWorkItemImportTargetFactory"/> and
/// <see cref="CompositeWorkItemRevisionSourceFactory"/>.
/// </summary>
public static class FactoryRegistrationExtensions
{
    /// <summary>
    /// Registers a concrete <see cref="IWorkItemImportTargetFactory"/> implementation
    /// and ensures the <see cref="CompositeWorkItemImportTargetFactory"/> dispatcher is
    /// registered as <see cref="IWorkItemImportTargetFactory"/>.
    /// </summary>
    public static IServiceCollection AddImportTargetFactory<TFactory>(
        this IServiceCollection services,
        string typeKey)
        where TFactory : class, IWorkItemImportTargetFactory
    {
        services.TryAddSingleton<TFactory>();
        services.AddSingleton(sp =>
            new KeyedWorkItemImportTargetFactory(typeKey, sp.GetRequiredService<TFactory>()));
        services.TryAddSingleton<IWorkItemImportTargetFactory, CompositeWorkItemImportTargetFactory>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IWorkItemRevisionSourceFactory"/> implementation
    /// and ensures the <see cref="CompositeWorkItemRevisionSourceFactory"/> dispatcher is
    /// registered as <see cref="IWorkItemRevisionSourceFactory"/>.
    /// Callers may register <typeparamref name="TFactory"/> with a different lifetime
    /// (e.g. scoped) before calling this method; the <c>TryAddSingleton</c> will be a no-op
    /// in that case.
    /// </summary>
    public static IServiceCollection AddRevisionSourceFactory<TFactory>(
        this IServiceCollection services,
        string typeKey)
        where TFactory : class, IWorkItemRevisionSourceFactory
    {
        services.TryAddSingleton<TFactory>();
        services.AddSingleton(new KeyedWorkItemRevisionSourceFactory(typeKey, typeof(TFactory)));
        services.TryAddScoped<IWorkItemRevisionSourceFactory, CompositeWorkItemRevisionSourceFactory>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IWorkItemResolutionStrategyFactory"/> that handles targets
    /// of type <typeparamref name="TTarget"/>, and ensures the
    /// <see cref="CompositeWorkItemResolutionStrategyFactory"/> dispatcher is registered as
    /// <see cref="IWorkItemResolutionStrategyFactory"/>.
    /// </summary>
    public static IServiceCollection AddResolutionStrategyFactory<TFactory, TTarget>(
        this IServiceCollection services)
        where TFactory : class, IWorkItemResolutionStrategyFactory
        where TTarget : IWorkItemImportTarget
    {
        services.TryAddSingleton<TFactory>();
        services.AddSingleton(sp =>
            new KeyedWorkItemResolutionStrategyFactory(
                target => target is TTarget,
                sp.GetRequiredService<TFactory>()));
        services.TryAddSingleton<IWorkItemResolutionStrategyFactory, CompositeWorkItemResolutionStrategyFactory>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IClassificationTreeReader"/> implementation keyed by
    /// <paramref name="typeKey"/> (the endpoint's <c>Type</c> discriminator, e.g.
    /// <c>"AzureDevOpsServices"</c> or <c>"Simulated"</c>), and ensures the
    /// <see cref="CompositeClassificationTreeReader"/> dispatcher is registered as
    /// <see cref="IClassificationTreeReader"/>.
    /// </summary>
    public static IServiceCollection AddClassificationTreeReader<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, IClassificationTreeReader
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(sp => new KeyedClassificationTreeReader(typeKey, sp.GetRequiredService<T>()));
        services.TryAddSingleton<IClassificationTreeReader, CompositeClassificationTreeReader>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="INodeCreator"/> implementation keyed by
    /// <paramref name="typeKey"/> (the endpoint's <c>Type</c> discriminator, e.g.
    /// <c>"AzureDevOpsServices"</c> or <c>"Simulated"</c>), and ensures the
    /// <see cref="CompositeNodeCreator"/> dispatcher is registered as <see cref="INodeCreator"/>.
    /// Also registers <see cref="NodeEnsurer"/> (if not already registered) so it can be
    /// optionally injected by <c>WorkItemsModule</c>.
    /// </summary>
    public static IServiceCollection AddNodeCreator<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, INodeCreator
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(sp => new KeyedNodeCreator(typeKey, sp.GetRequiredService<T>()));
        services.TryAddSingleton<INodeCreator, CompositeNodeCreator>();
        services.TryAddSingleton<NodeEnsurer>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IIdentitySource"/> implementation keyed by
    /// <paramref name="typeKey"/> and ensures the <see cref="CompositeIdentitySource"/>
    /// dispatcher is registered as <see cref="IIdentitySource"/>.
    /// </summary>
    public static IServiceCollection AddIdentitySource<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, IIdentitySource
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(new KeyedIdentitySource(typeKey, typeof(T)));
        services.TryAddSingleton<IIdentitySource, CompositeIdentitySource>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="ITeamSource"/> implementation keyed by
    /// <paramref name="typeKey"/> and ensures the <see cref="CompositeTeamSource"/>
    /// dispatcher is registered as <see cref="ITeamSource"/>.
    /// </summary>
    public static IServiceCollection AddTeamSource<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, ITeamSource
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(new KeyedTeamSource(typeKey, typeof(T)));
        services.TryAddSingleton<ITeamSource, CompositeTeamSource>();
        return services;
    }
}
#endif
