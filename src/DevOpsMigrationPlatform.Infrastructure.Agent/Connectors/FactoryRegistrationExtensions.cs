// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemType;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Extension methods for registering connector-specific factory implementations with the
/// composite dispatchers <see cref="CompositeWorkItemTargetFactory"/> and
/// <see cref="CompositeWorkItemRevisionSourceFactory"/>.
/// </summary>
public static class FactoryRegistrationExtensions
{
    /// <summary>
    /// Registers a concrete <see cref="IWorkItemTargetFactory"/> implementation
    /// and ensures the <see cref="CompositeWorkItemTargetFactory"/> dispatcher is
    /// registered as <see cref="IWorkItemTargetFactory"/>.
    /// </summary>
    public static IServiceCollection AddImportTargetFactory<TFactory>(
        this IServiceCollection services,
        string typeKey)
        where TFactory : class, IWorkItemTargetFactory
    {
        services.TryAddSingleton<TFactory>();
        services.AddSingleton(sp =>
            new KeyedWorkItemTargetFactory(typeKey, sp.GetRequiredService<TFactory>()));
        services.TryAddSingleton<IWorkItemTargetFactory, CompositeWorkItemTargetFactory>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IWorkItemTypeReadinessTargetFactory"/> implementation
    /// and ensures the <see cref="CompositeWorkItemTypeReadinessTargetFactory"/> dispatcher is
    /// registered as <see cref="IWorkItemTypeReadinessTargetFactory"/>.
    /// </summary>
    public static IServiceCollection AddWorkItemTypeReadinessTargetFactory<TFactory>(
        this IServiceCollection services,
        string typeKey)
        where TFactory : class, IWorkItemTypeReadinessTargetFactory
    {
        services.TryAddSingleton<TFactory>();
        services.AddSingleton(sp =>
            new KeyedWorkItemTypeReadinessTargetFactory(typeKey, sp.GetRequiredService<TFactory>()));
        services.TryAddSingleton<IWorkItemTypeReadinessTargetFactory, CompositeWorkItemTypeReadinessTargetFactory>();
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
        where TTarget : IWorkItemTarget
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
    /// </summary>
    public static IServiceCollection AddNodeCreator<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, INodeCreator
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(sp => new KeyedNodeCreator(typeKey, sp.GetRequiredService<T>()));
        services.TryAddSingleton<INodeCreator, CompositeNodeCreator>();
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

    /// <summary>
    /// Registers a concrete <see cref="ITeamTarget"/> implementation keyed by
    /// <paramref name="typeKey"/> (the endpoint's <c>Type</c> discriminator, e.g.
    /// <c>"AzureDevOpsServices"</c> or <c>"Simulated"</c>), and ensures the
    /// <see cref="CompositeTeamTarget"/> dispatcher is registered as <see cref="ITeamTarget"/>.
    /// </summary>
    public static IServiceCollection AddTeamTarget<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, ITeamTarget
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(sp => new KeyedTeamTarget(typeKey, sp.GetRequiredService<T>()));
        services.TryAddSingleton<ITeamTarget, CompositeTeamTarget>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IWorkItemDiscoveryService"/> implementation keyed by
    /// <paramref name="typeKey"/> and ensures the <see cref="CompositeWorkItemDiscoveryService"/>
    /// dispatcher is registered as <see cref="IWorkItemDiscoveryService"/>.
    /// </summary>
    public static IServiceCollection AddWorkItemDiscoveryService<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, IWorkItemDiscoveryService
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(new KeyedWorkItemDiscoveryService(typeKey, typeof(T)));
        services.TryAddSingleton<IWorkItemDiscoveryService, CompositeWorkItemDiscoveryService>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IProjectLifecycleProvider"/> keyed by
    /// <paramref name="typeKey"/> and ensures <see cref="ProjectLifecycleService"/> is registered
    /// as <see cref="IProjectLifecycleService"/>.
    /// </summary>
    public static IServiceCollection AddProjectLifecycleProvider<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, IProjectLifecycleProvider
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(new KeyedProjectLifecycleProvider(typeKey, typeof(T)));
        services.TryAddSingleton<IProjectLifecycleService, ProjectLifecycleService>();
        services.TryAddSingleton<IProjectLifecycleNameGenerator, ProjectLifecycleNameGenerator>();
        services.TryAddSingleton<ProjectLifecycleProgressEmitter>();
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="IProjectProcessProvider"/> keyed by
    /// <paramref name="typeKey"/> and ensures <see cref="ProjectProcessService"/> is registered
    /// as <see cref="IProjectProcessService"/>.
    /// </summary>
    public static IServiceCollection AddProjectProcessProvider<T>(
        this IServiceCollection services,
        string typeKey)
        where T : class, IProjectProcessProvider
    {
        services.TryAddSingleton<T>();
        services.AddSingleton(new KeyedProjectProcessProvider(typeKey, typeof(T)));
        services.TryAddSingleton<IProjectProcessService, ProjectProcessService>();
        return services;
    }
}
