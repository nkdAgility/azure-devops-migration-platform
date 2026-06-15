// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Nodes;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemType;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Registers module implementations (<see cref="IModule"/> and <see cref="IDiscoveryModule"/>)
/// with the DI container. These registrations belong at the composition root — NOT inside
/// connector assemblies — so that connectors remain decoupled from module implementations.
/// </summary>
public static class ModuleServiceCollectionExtensions
{
    /// <summary>
    /// Convenience method that registers all agent modules and their orchestrators.
    /// Calls <see cref="AddWorkItemsModule"/>, <see cref="AddInventoryModule"/>,
    /// <see cref="AddDependenciesModule"/>, <see cref="AddNodesModule"/>,
    /// <see cref="AddIdentitiesModule"/>, and <see cref="TeamsServiceCollectionExtensions.AddTeamsModule"/>.
    /// </summary>
    public static IServiceCollection AddAllAgentModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddWorkItemsModule(configuration);
        services.AddInventoryOrchestratorServices();
        services.AddInventoryAnalyserServices();
        services.AddDependencyAnalyserServices();
        services.AddNodesModule(configuration);
        services.AddIdentitiesModule(configuration);
        services.AddTeamsModule(configuration);
        return services;
    }

    /// <summary>
    /// Registers <see cref="WorkItemsModule"/> as the <see cref="IModule"/> implementation
    /// for work item export/import operations.
    /// </summary>
    public static IServiceCollection AddWorkItemsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<WorkItemsModuleOptions>("Work items export/import module configuration");
        services.AddSchemaEntry<WorkItemOptions>("Work item import replay lever configuration");
#endif
        services.TryAddSingleton<ICurrentPackageConfigAccessor, CurrentPackageConfigAccessor>();
        services.AddOptions<WorkItemsModuleOptions>()
            .Configure<ICurrentPackageConfigAccessor>((opts, state) =>
            {
                state.Current?.GetSection(WorkItemsModuleOptions.SectionName).Bind(opts);
            });

        services.RegisterWorkItemServices(configuration);

        // Work-item capability extensions (IModuleExtension ports). Each owns its own IOptions<T>.
#if NET7_0_OR_GREATER
        services.AddSchemaEntry<LinksExtensionOptions>("Work item Links extension (related/external/hyperlinks) configuration");
#endif
        services.AddOptions<LinksExtensionOptions>()
            .Configure<IOptions<WorkItemsModuleOptions>>((o, wi) => o.Enabled = wi.Value.Extensions.Links.Enabled);
        services.AddSingleton<LinksWorkItemExtension>();

#if NET7_0_OR_GREATER
        services.AddSchemaEntry<AttachmentsExtensionOptions>("Work item Attachments extension (attachment binary replay) configuration");
#endif
        services.AddOptions<AttachmentsExtensionOptions>()
            .Configure<IOptions<WorkItemsModuleOptions>>((o, wi) => o.Enabled = wi.Value.Extensions.Attachments.Enabled);
        services.AddSingleton<AttachmentsWorkItemExtension>();

#if NET7_0_OR_GREATER
        services.AddSchemaEntry<CommentsExtensionOptions>("Work item Comments extension (inline comment replay) configuration");
#endif
        services.AddOptions<CommentsExtensionOptions>()
            .Configure<IOptions<WorkItemsModuleOptions>>((o, wi) => o.Enabled = wi.Value.Extensions.Comments.Enabled);
        services.AddSingleton<CommentsWorkItemExtension>();

        services.TryAddSingleton<IWorkItemExportOrchestratorFactory, WorkItemExportOrchestratorFactory>();
        services.AddScoped<IWorkItemsImportCapabilityValidator, WorkItemsImportCapabilityValidator>();
        services.AddSingleton<IWorkItemsNodeReadinessOrchestrator>(sp =>
            new WorkItemsNodeReadinessOrchestrator(
                sp.GetService<NodeReadinessOrchestrator>(),
                sp.GetService<INodesOrchestrator>(),
                sp.GetService<IPlatformMetrics>(),
                sp.GetRequiredService<ILogger<WorkItemsModule>>()));
        services.AddScoped<ImportPreparer>(sp =>
        {
            var patterns = sp.GetServices<IImportFailurePattern>()?.ToArray();
            var resolved = patterns is { Length: > 0 }
                ? patterns
                : new IImportFailurePattern[]
                {
                    new MissingRevisionArtefactImportFailurePattern(),
                    new InvalidRevisionPayloadImportFailurePattern(),
                    new MissingAttachmentBinaryImportFailurePattern(),
                    new MissingEmbeddedImageBinaryImportFailurePattern(),
                    new FieldTransformCompatibilityImportFailurePattern()
                };
            var sourceEndpointInfo = sp.GetRequiredService<ISourceEndpointInfo>();
            return new ImportPreparer(
                sp.GetRequiredService<IOptions<WorkItemsModuleOptions>>(),
                sourceEndpointInfo.OrganisationSlug,
                sourceEndpointInfo.Project,
                resolved);
        });
        services.AddScoped<WorkItemsOrchestrator>(sp =>
            new WorkItemsOrchestrator(
                sp.GetRequiredService<IWorkItemRevisionSourceFactory>(),
                sp.GetService<IAttachmentBinarySource>(),
                sp.GetService<IWorkItemCommentSourceFactory>(),
                sp.GetService<IWorkItemFetchService>(),
                sp.GetRequiredService<IWorkItemExportOrchestratorFactory>(),
                sp.GetRequiredService<ICheckpointingServiceFactory>(),
                sp.GetRequiredService<ILogger<WorkItemsModule>>(),
                sp.GetService<IPlatformMetrics>(),
                sp.GetService<IWorkItemDiscoveryService>(),
                sp.GetService<IExportProgressStoreFactory>(),
                sp.GetService<IReferencedPathTracker>(),
                sp.GetRequiredService<IOptions<WorkItemsModuleOptions>>(),
                sp.GetRequiredService<ISourceEndpointInfo>(),
                sp.GetRequiredService<ImportPreparer>(),
                sp.GetRequiredService<IWorkItemTargetFactory>(),
                sp.GetRequiredService<IWorkItemResolutionStrategyFactory>(),
                sp.GetRequiredService<IIdMapStoreFactory>(),
                sp.GetRequiredService<IWorkItemResolutionProcessorFactory>(),
                sp.GetService<IIdentityTranslationTool>(),
                sp.GetRequiredService<IWorkItemsImportCapabilityValidator>(),
                sp.GetRequiredService<IWorkItemsNodeReadinessOrchestrator>(),
                sp.GetRequiredService<ITargetEndpointInfo>(),
                sp.GetService<IOptions<WorkItemOptions>>(),
                sp.GetService<IOptions<NodesModuleOptions>>(),
                sp.GetService<IInventoryOrchestrator>(),
                sp.GetService<IRepoDiscoveryService>(),
                sp.GetRequiredService<AttachmentsWorkItemExtension>(),
                sp.GetRequiredService<CommentsWorkItemExtension>()));
        services.AddScoped<IWorkItemsOrchestrator>(sp => sp.GetRequiredService<WorkItemsOrchestrator>());
        services.AddTransient<IModule, WorkItemsModule>();
        return services;
    }

    /// <summary>
    /// Registers inventory orchestration services used by module-level inventory operations.
    /// </summary>
    public static IServiceCollection AddInventoryOrchestratorServices(this IServiceCollection services)
    {
        services.AddSingleton<IInventoryOrchestrator, InventoryOrchestrator>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="NodesModule"/> as an <see cref="IModule"/> implementation
    /// for classification tree export/import operations.
    /// </summary>
    public static IServiceCollection AddNodesModule(this IServiceCollection services, IConfiguration configuration)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<NodesModuleOptions>("Classification nodes (area/iteration paths) module configuration");
#endif

        services.AddScoped<INodesOrchestrator, NodesOrchestrator>();
        services.AddTransient<IModule, NodesModule>();
        services.Configure<NodesModuleOptions>(
            configuration.GetSection(NodesModuleOptions.SectionName));
        return services;
    }
}
