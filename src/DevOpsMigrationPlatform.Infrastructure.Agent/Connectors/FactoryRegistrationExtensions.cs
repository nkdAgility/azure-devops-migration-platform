#if !NET481
using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
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
}
#endif
