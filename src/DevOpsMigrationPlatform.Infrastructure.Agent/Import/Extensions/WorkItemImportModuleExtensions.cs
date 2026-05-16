// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Extensions;

/// <summary>
/// Registers WorkItem import services at the module composition boundary.
/// </summary>
public static class WorkItemImportModuleExtensions
{
    /// <summary>
    /// Registers WorkItem import options, validation, and prepare-time failure patterns.
    /// </summary>
    public static IServiceCollection RegisterWorkItemImportServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddSingleton<IValidateOptions<WorkItemImportOptions>, WorkItemImportOptionsValidator>();

        var workItemImportOptionsBuilder = services.AddOptions<WorkItemImportOptions>();
        if (configuration is not null)
        {
            workItemImportOptionsBuilder.Bind(configuration.GetSection(WorkItemImportOptions.SectionName));
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
#if !NET481
        services.AddSingleton<NodeReadinessOrchestrator>();
#endif

        return services;
    }
}
