// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Identity;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Nodes;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemType;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Teams;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Self-registration entry point for the Infrastructure.TfsObjectModel module.
/// Encapsulates all module-internal DI registrations (per-job TFS Object Model
/// service factory, ambient job state, and the TFS adapter implementations for
/// the module contracts) so hosts compose the module with a single call.
/// </summary>
public static class TfsObjectModelModuleServiceExtensions
{
    public static IServiceCollection AddTfsObjectModelModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        // Per-job TFS Object Model service factory — creates TFS connections, revision sources,
        // attachment sources, tree readers, and discovery services per job based on the endpoint.
        services.AddSingleton<DevOpsMigrationPlatform.Abstractions.Agent.TfsExecution.ITfsJobServiceFactory, TfsJobServiceFactory>();

        // Ambient state carrying the current job's TFS services (set by the TFS job worker before running modules).
        services.AddSingleton<ActiveTfsJobServices>();

        // TFS adapter implementations for module contracts.
        services.AddSingleton<IClassificationTreeCapture, TfsClassificationTreeCapture>();
        services.AddSingleton<IWorkItemRevisionSourceFactory, TfsActiveJobWorkItemRevisionSourceFactory>();
        services.AddSingleton<IIdentitySource, TfsActiveJobIdentitySource>();
        // TFS identity adapter (reduced capability: empty + Warning) for full connector coverage (FR-019).
        // Registered via the keyed composite dispatch seam so identity lookups route by connector type.
        services.AddIdentityAdapter<TfsIdentityAdapter>("TeamFoundationServer");
        services.AddSingleton<ITeamSource, TfsActiveJobTeamSource>();
        // TFS has no board API - register explicit None capability and null adapter so
        // BoardConfigTeamExtension can be constructed via DI; the capability check fires first.
        services.AddSingleton<global::DevOpsMigrationPlatform.Abstractions.Agent.IConnectorCapabilityProvider,
            TfsConnectorCapabilityProvider>();
        services.AddSingleton<global::DevOpsMigrationPlatform.Abstractions.Agent.Teams.ITeamBoardAdapter,
            TfsNullBoardAdapter>();
        services.AddSingleton<INodeCreator, TfsActiveJobNodeCreator>();
        services.AddSingleton<TfsActiveJobWorkItemTypeReadinessTargetFactory>();
        services.TryAddSingleton<IWorkItemTypeReadinessTargetFactory>(sp => sp.GetRequiredService<TfsActiveJobWorkItemTypeReadinessTargetFactory>());

        // TFS work item import target — creates work items in the TFS/ADO target via TFS Object Model.
        services.AddSingleton<TfsActiveJobWorkItemTargetFactory>();
        services.AddImportTargetFactory<TfsActiveJobWorkItemTargetFactory>("TeamFoundationServer");

        // TFS work item resolution strategy — idmap-based duplicate detection, no external lookup needed.
        services.AddResolutionStrategyFactory<TfsResolutionStrategyFactory, TfsWorkItemTarget>();

        // Connector-keyed node creator, project lifecycle, and process providers.
        services.AddNodeCreator<TfsActiveJobNodeCreator>("TeamFoundationServer");
        services.AddProjectLifecycleProvider<TfsProjectLifecycleProvider>("TeamFoundationServer");
        services.AddProjectProcessProvider<TfsProjectProcessProvider>("TeamFoundationServer");

        // TFS source endpoint info — reads from ActiveTfsJobServices (source-only, no target).
        services.AddTfsSourceEndpointInfo();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISourceEndpointInfo"/> for the TFS connector (source-only).
    /// Reads values from <see cref="ActiveTfsJobServices"/>, which is populated by
    /// the TFS job worker when a job is picked up.
    /// </summary>
    private static IServiceCollection AddTfsSourceEndpointInfo(this IServiceCollection services)
    {
        services.TryAddSingleton<ISourceEndpointInfo>(sp =>
        {
            var activeServices = sp.GetRequiredService<ActiveTfsJobServices>();
            return new DeferredTfsSourceEndpointInfo(activeServices);
        });

        return services;
    }

    /// <summary>
    /// Deferred implementation of <see cref="ISourceEndpointInfo"/> for TFS.
    /// Reads from <see cref="ActiveTfsJobServices"/> at property-access time,
    /// not at DI resolution time, so that it works for Import jobs where no
    /// TFS Object Model connection is established.
    /// </summary>
    private sealed class DeferredTfsSourceEndpointInfo : ISourceEndpointInfo
    {
        private readonly ActiveTfsJobServices _activeServices;

        public DeferredTfsSourceEndpointInfo(ActiveTfsJobServices activeServices)
            => _activeServices = activeServices;

        public string Url
            => _activeServices.Current?.Endpoint.GetResolvedUrl() ?? string.Empty;

        public string Project
            => _activeServices.Current?.Endpoint.GetProject() ?? string.Empty;

        public string ConnectorType => "TeamFoundationServer";

        public string OrganisationSlug => OrganisationEndpointSlug.ExtractSlug(Url);

        public OrganisationEndpoint ToOrganisationEndpoint()
            => new OrganisationEndpoint { ResolvedUrl = Url, Type = ConnectorType };
    }
}
