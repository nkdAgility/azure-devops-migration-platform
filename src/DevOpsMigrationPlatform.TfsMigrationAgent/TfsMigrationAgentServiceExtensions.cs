// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

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

        // Package storage and config store — filesystem store must be registered before config loader.
        services.AddPackageStorageServices();
        services.AddPackageMigrationConfigLoader();

        // Project lifecycle name generator (Infrastructure.Agent implementation).
        services.AddSingleton<IProjectLifecycleNameGenerator, ProjectLifecycleNameGenerator>();

        // TFS Object Model module — self-registers its per-job service factory, ambient job state,
        // adapter implementations for module contracts, connector-keyed providers, and source endpoint info.
        services.AddTfsObjectModelModule(configuration);

        // Import infrastructure: idmap store, revision processor.
        services.AddSingleton<IIdMapStoreFactory, IdMapStoreFactory>();
        services.AddScoped<IWorkItemResolutionProcessorFactory, RevisionFolderProcessorFactory>();

        // Field transform and node translation tools for import.
        services.AddFieldTransformToolServices();
        services.AddNodeTranslationToolServices();

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

        // Target endpoint info — reads from ICurrentJobEndpointAccessor (set by base class from config).
        services.TryAddSingleton<ITargetEndpointInfo, ActiveJobTargetEndpointInfo>();

        // Unified worker — polls /agents/lease?capabilities=tfs and dispatches to TFS execution.
        services.AddSingleton<IHostedService, TfsJobAgentWorker>();

        return services;
    }
}
