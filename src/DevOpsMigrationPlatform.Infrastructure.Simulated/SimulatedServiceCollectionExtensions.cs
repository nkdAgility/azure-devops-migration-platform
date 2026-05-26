// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Export;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Factories;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated;

/// <summary>
/// Extension methods for registering Simulated connector services.
/// </summary>
public static class SimulatedServiceCollectionExtensions
{
    /// <summary>
    /// Registers Simulated export services:
    /// <list type="bullet">
    ///   <item><see cref="SimulatedEndpointOptions"/> and <see cref="SimulatedOrganisationEntry"/> for polymorphic JSON deserialization.</item>
    ///   <item><see cref="SimulatedWorkItemRevisionSourceFactory"/> as a keyed export source factory for <c>"Simulated"</c>.</item>
    ///   <item>Discovery and attachment services for Simulated sources.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddSimulatedWorkItemExport(this IServiceCollection services)
    {
        // Register endpoint and org entry types for polymorphic JSON deserialization
        services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions));
        services.AddOrganisationEntryType("Simulated", typeof(SimulatedOrganisationEntry));

        // Register revision source factory for export
        services.AddRevisionSourceFactory<SimulatedWorkItemRevisionSourceFactory>("Simulated");

        // Classification tree reader — returns a minimal deterministic tree for simulated sources.
        services.AddClassificationTreeReader<SimulatedClassificationTreeReader>("Simulated");

        // Identity source — deterministic simulated identities keyed by connector type.
        services.AddIdentitySource<SimulatedIdentitySource>("Simulated");

        // Team source — deterministic simulated teams keyed by connector type.
        services.AddTeamSource<SimulatedTeamSource>("Simulated");

        // Discovery services (for inventory of simulated sources).
        // Pre-register concrete type with factory to resolve the two-constructor ambiguity,
        // then register via the composite pattern so the correct impl is dispatched for
        // "Simulated" connector type.
        services.TryAddSingleton<SimulatedGeneratorConfig>();
        services.TryAddSingleton<IProjectDiscoveryService, SimulatedProjectDiscoveryService>();
        services.TryAddSingleton(sp =>
            new SimulatedWorkItemDiscoveryService(sp.GetRequiredService<ICurrentPackageConfigAccessor>()));
        services.AddWorkItemDiscoveryService<SimulatedWorkItemDiscoveryService>("Simulated");

        // Keyed discovery services — always resolve to simulated impls regardless of other registrations.
        // These are consumed by SimulatedInventoryServiceFactory via [FromKeyedServices("Simulated")].
        services.AddKeyedSingleton<IProjectDiscoveryService, SimulatedProjectDiscoveryService>("Simulated");
        services.AddKeyedSingleton<IWorkItemDiscoveryService>("Simulated", (sp, _) =>
            new SimulatedWorkItemDiscoveryService(sp.GetRequiredService<ICurrentPackageConfigAccessor>()));
        services.AddKeyedSingleton<IRepoDiscoveryService, SimulatedRepoDiscoveryService>("Simulated");

        // Keyed inventory factory — used by InventoryDiscoveryModule when connector type is "Simulated".
        services.AddKeyedSingleton<IInventoryServiceFactory, SimulatedInventoryServiceFactory>("Simulated");

        return services;
    }

    /// <summary>
    /// Registers Simulated import services:
    /// <list type="bullet">
    ///   <item><see cref="SimulatedWorkItemImportTargetFactory"/> as a keyed import target factory for <c>"Simulated"</c>.</item>
    ///   <item><see cref="SimulatedResolutionStrategyFactory"/> for <see cref="SimulatedWorkItemImportTarget"/> targets.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddSimulatedWorkItemImport(this IServiceCollection services)
    {
        // Register import target factory as keyed entry in the composite dispatcher
        services.AddImportTargetFactory<SimulatedWorkItemImportTargetFactory>("Simulated");
        services.AddWorkItemTypeReadinessTargetFactory<SimulatedWorkItemTypeReadinessTargetFactory>("Simulated");

        // Register resolution strategy factory — always returns NullResolutionStrategy
        services.AddResolutionStrategyFactory<SimulatedResolutionStrategyFactory, SimulatedWorkItemImportTarget>();

        // Classification node creator — in-memory simulation of node creation.
        services.AddNodeCreator<SimulatedNodeCreator>("Simulated");

        // Team target — in-memory simulation of team creation, keyed for composite dispatch.
        services.AddTeamTarget<SimulatedTeamTarget>("Simulated");
        services.AddProjectLifecycleProvider<SimulatedProjectLifecycleProvider>("Simulated");
        services.AddProjectProcessProvider<SimulatedProjectProcessProvider>("Simulated");

        return services;
    }

    /// <summary>
    /// Registers Simulated dependency analysis services.
    /// The Simulated link analysis service returns empty link results.
    /// Also registers <see cref="SimulatedDependencyDiscoveryServiceFactory"/> as
    /// <see cref="IDependencyDiscoveryServiceFactory"/> via <c>TryAddSingleton</c> so it only
    /// takes effect when no ADO factory is registered.
    /// </summary>
    public static IServiceCollection AddSimulatedDependencyAnalysis(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService, SimulatedWorkItemLinkAnalysisService>(
            serviceKey: "Simulated");
        services.TryAddSingleton<IDependencyDiscoveryServiceFactory, SimulatedDependencyDiscoveryServiceFactory>();
        return services;
    }

    /// <summary>
    /// Registers all Simulated connector services (export, import, and dependency analysis).
    /// Convenience method that calls <see cref="AddSimulatedWorkItemExport"/>,
    /// <see cref="AddSimulatedWorkItemImport"/>, and <see cref="AddSimulatedDependencyAnalysis"/>.
    /// </summary>
    public static IServiceCollection AddSimulatedServices(this IServiceCollection services)
    {
        services.AddSimulatedWorkItemExport();
        services.AddSimulatedWorkItemImport();
        services.AddSimulatedDependencyAnalysis();
        services.AddSimulatedEndpointInfo();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ISourceEndpointInfo"/> and <see cref="ITargetEndpointInfo"/> for the Simulated connector.
    /// Uses TryAddSingleton so the dynamic active-job implementations registered
    /// by the MigrationAgent take precedence when both connectors are in the same host.
    /// </summary>
    private static IServiceCollection AddSimulatedEndpointInfo(this IServiceCollection services)
    {
        // Source endpoint info — TryAdd so dynamic per-job impl takes precedence
        services.TryAddSingleton<ISourceEndpointInfo>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SimulatedEndpointOptions>>().Value;
            return new SimulatedSourceEndpointInfo
            {
                Url = opts.GetResolvedUrl(),
                Project = opts.GetProject(),
                ConnectorType = "Simulated"
            };
        });

        // Target endpoint info — TryAdd so dynamic per-job impl takes precedence
        services.TryAddSingleton<ITargetEndpointInfo>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SimulatedEndpointOptions>>().Value;
            return new SimulatedTargetEndpointInfo
            {
                Url = opts.GetResolvedUrl(),
                Project = opts.GetProject(),
                ConnectorType = "Simulated"
            };
        });

        return services;
    }

    /// <summary>
    /// Inline implementation of <see cref="ISourceEndpointInfo"/> for Simulated connector.
    /// </summary>
    private sealed record SimulatedSourceEndpointInfo : ISourceEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }

        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType
        };
    }

    /// <summary>
    /// Inline implementation of <see cref="ITargetEndpointInfo"/> for Simulated connector.
    /// </summary>
    private sealed record SimulatedTargetEndpointInfo : ITargetEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }

        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType
        };
    }
}
