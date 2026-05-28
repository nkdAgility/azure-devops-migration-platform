// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;

namespace DevOpsMigrationPlatform.TfsMigrationAgent;

/// <summary>
/// Registers all TFS Migration Agent services into the host's DI container.
/// Structural twin of <c>MigrationAgentServiceExtensions</c> but targets net481
/// and uses <see cref="IServiceCollection"/> directly (no <c>IHostApplicationBuilder</c>
/// on net481).
/// </summary>
public static class TfsMigrationAgentServiceExtensions
{
    public static IServiceCollection AddTfsMigrationAgentServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri controlPlaneBaseUrl)
    {
        // Core shared services — ambient state, telemetry, HTTP clients, progress sinks,
        // store factories, diagnostics, and the telemetry push timer.
        // No Polly resilience handler on net481 — simple retry in the polling loop is sufficient
        // for localhost communication.
        services.AddCoreAgentServices(configuration, controlPlaneBaseUrl);

        // Package config store — reads migration-config.json from the package at job pickup.
        services.AddPackageMigrationConfigLoader();

        // Per-job TFS Object Model service factory — creates TFS connections, revision sources,
        // attachment sources, tree readers, and discovery services per job based on the endpoint.
        services.AddSingleton<IProjectLifecycleNameGenerator, ProjectLifecycleNameGenerator>();
        services.AddSingleton<ITfsJobServiceFactory, TfsJobServiceFactory>();

        // Ambient state carrying the current job's TFS services (set by TfsJobAgentWorker before running modules).
        services.AddSingleton<ActiveTfsJobServices>();

        // TFS adapter implementations for module contracts.
        services.AddSingleton<IClassificationTreeCapture, TfsClassificationTreeCapture>();
        services.AddSingleton<IWorkItemRevisionSourceFactory, TfsActiveJobWorkItemRevisionSourceFactory>();
        services.AddSingleton<IIdentitySource, TfsActiveJobIdentitySource>();
        services.AddSingleton<ITeamSource, TfsActiveJobTeamSource>();
        services.AddSingleton<INodeCreator, TfsActiveJobNodeCreator>();
        services.AddSingleton<TfsActiveJobWorkItemTypeReadinessTargetFactory>();
        services.TryAddSingleton<IWorkItemTypeReadinessTargetFactory>(sp => sp.GetRequiredService<TfsActiveJobWorkItemTypeReadinessTargetFactory>());

        // TFS work item import target — creates work items in the TFS/ADO target via TFS Object Model.
        services.AddSingleton<TfsActiveJobWorkItemTargetFactory>();
        services.AddImportTargetFactory<TfsActiveJobWorkItemTargetFactory>("TeamFoundationServer");

        // TFS work item resolution strategy — idmap-based duplicate detection, no external lookup needed.
        services.AddResolutionStrategyFactory<TfsResolutionStrategyFactory, TfsWorkItemTarget>();

        // Import infrastructure: idmap store, revision processor, node creator for import.
        services.AddSingleton<IIdMapStoreFactory, IdMapStoreFactory>();
        services.AddScoped<IWorkItemResolutionProcessorFactory, RevisionFolderProcessorFactory>();
        services.AddNodeCreator<TfsActiveJobNodeCreator>("TeamFoundationServer");
        services.AddProjectLifecycleProvider<TfsProjectLifecycleProvider>("TeamFoundationServer");
        services.AddProjectProcessProvider<TfsProjectProcessProvider>("TeamFoundationServer");

        // Field transform and node translation tools for import.
        services.AddFieldTransformToolServices();
        services.AddNodeTranslationToolServices();
        // On net481, AddNodeTranslationToolServices() does not register INodeTranslationTool.
        // Register NodeTranslationTool directly so NodeReadinessOrchestrator can be activated.
        services.TryAddSingleton<NodeTranslationTool>(sp => new NodeTranslationTool(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DevOpsMigrationPlatform.Abstractions.Options.NodeTranslationOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NodeTranslationTool>>(),
            null));
        services.TryAddSingleton<INodeTranslationTool>(sp => sp.GetRequiredService<NodeTranslationTool>());

        // Export progress store — SQLite-backed fast-forward resume (now supported on net481).
        services.AddSingleton<IExportProgressStoreFactory, ExportProgressStoreFactory>();

        // Register IModule pipeline (export-only on net481 for Teams/WorkItems/Nodes/Identities).
        services.AddIdentitiesModule(configuration);
        services.AddNodesModule(configuration);
        services.AddTeamsModule(configuration);
        services.AddWorkItemsModule(configuration);

        // NOTE (spec 032, D-007): AddDependencyCapture() is intentionally NOT called here.
        // TFS sources do not support per-project dependency capture; the TFS plan builder emits
        // no capture.dependencies.* tasks. If any capture.dependencies.* task were ever queued
        // for a TFS job, the JobPlanExecutor captureHandlersByName missing-handler path
        // (log Error + skip, no throw) would handle it gracefully.

        // TFS source endpoint info — reads from ActiveTfsJobServices (source-only, no target).
        services.AddTfsSourceEndpointInfo();

        // Target endpoint info — reads from ICurrentJobEndpointAccessor (set by base class from config).
        services.TryAddSingleton<ITargetEndpointInfo, ActiveJobTargetEndpointInfo>();

        // Unified worker — polls /agents/lease?capabilities=tfs and dispatches to TFS execution.
        services.AddSingleton<IHostedService, TfsJobAgentWorker>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISourceEndpointInfo"/> for the TFS connector (source-only).
    /// Reads values from <see cref="ActiveTfsJobServices"/>, which is populated by
    /// <see cref="TfsJobAgentWorker"/> when a job is picked up.
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

        public string OrganisationSlug => EndpointSlugHelper.ExtractSlug(Url);

        public OrganisationEndpoint ToOrganisationEndpoint()
            => new OrganisationEndpoint { ResolvedUrl = Url, Type = ConnectorType };
    }
}
