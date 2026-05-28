// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Nodes;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemType;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Registers WorkItem import services at the module composition boundary.
/// </summary>
public static class WorkItemModuleExtensions
{
    /// <summary>
    /// Registers WorkItem import options, validation, and prepare-time failure patterns.
    /// </summary>
    public static IServiceCollection RegisterWorkItemServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddSingleton<IValidateOptions<WorkItemOptions>, WorkItemOptionsValidator>();

        var workItemImportOptionsBuilder = services.AddOptions<WorkItemOptions>();
        if (configuration is not null)
        {
            workItemImportOptionsBuilder.Bind(configuration.GetSection(WorkItemOptions.SectionName));
        }
        workItemImportOptionsBuilder.ValidateOnStart();

        services.AddTransient<IImportFailurePattern, MissingRevisionArtefactImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, InvalidRevisionPayloadImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, MissingAttachmentBinaryImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, MissingEmbeddedImageBinaryImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, FieldTransformCompatibilityImportFailurePattern>();
        services.AddTransient<IImportFailurePattern, NodePathValidator>();
        services.AddTransient<IImportFailurePattern, WorkItemTypeValidator>();
        services.AddTransient<IImportFailurePattern, IdentityMappingValidator>();
        services.AddSingleton<ImportWorkItemStateStore>();
        services.AddSingleton<IImportCreatedNodeStateStore>(serviceProvider => serviceProvider.GetRequiredService<ImportWorkItemStateStore>());
        services.AddSingleton<NodeReadinessOrchestrator>();

        return services;
    }
}
