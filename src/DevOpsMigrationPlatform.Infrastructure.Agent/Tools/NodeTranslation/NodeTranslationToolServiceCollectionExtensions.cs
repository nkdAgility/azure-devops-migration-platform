// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// DI registration for the NodeTranslation tool and its supporting services.
/// </summary>
public static class NodeTranslationToolServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="INodeTranslationTool"/>, <see cref="INodeTranslationTool"/>, and <see cref="INodeTranslationValidator"/>
    /// to the service collection.
    /// <para>
    /// <see cref="INodeCreator"/> and <see cref="IClassificationTreeReader"/> are
    /// connector-specific and must be registered by the connector DI
    /// (e.g. <c>AddAzureDevOpsWorkItem</c>, <c>AddSimulatedWorkItem</c>,
    /// <c>TfsClassificationTreeReader</c>).
    /// </para>
    /// <para>
    /// Tools are registered as <b>Singleton</b> to satisfy singleton planning-pipeline
    /// consumers that resolve translation services during host startup.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNodeTranslationToolServices(this IServiceCollection services)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<NodeTranslationOptions>("Classification node path translation configuration");
#endif

        services.TryAddSingleton<ICurrentPackageConfigAccessor, CurrentPackageConfigAccessor>();

        // Bind from the explicit current package config set for the current job.
        services.AddOptions<NodeTranslationOptions>()
            .Configure<ICurrentPackageConfigAccessor>((opts, state) =>
            {
                state.Current?.GetSection(NodeTranslationOptions.SectionName).Bind(opts);
            });

#if !NET481
        services.AddSingleton<IValidateOptions<NodeTranslationOptions>, NodeTranslationOptionsValidator>();
        services.AddSingleton<NodeTranslationTool>(sp => new NodeTranslationTool(
            sp.GetRequiredService<IOptions<NodeTranslationOptions>>(),
            sp.GetRequiredService<ILogger<NodeTranslationTool>>(),
            sp.GetService<IPlatformMetrics>()));
        services.AddSingleton<INodeTranslationTool>(sp => sp.GetRequiredService<NodeTranslationTool>());
        services.AddScoped<INodeTranslationValidator>(sp => new NodeTranslationValidator(
            sp.GetRequiredService<IOptions<NodeTranslationOptions>>(),
            sp.GetRequiredService<INodeTranslationTool>()));
        // T012: ReferencedPathTracker is Scoped so the same path set is shared within one job
        // (scope) and isolated across jobs.
        services.AddScoped<ReferencedPathTracker>();
        services.AddScoped<IReferencedPathTracker>(sp => sp.GetRequiredService<ReferencedPathTracker>());
        services.AddScoped<ClassificationTreeCapture>();
        services.AddScoped<IClassificationTreeCapture>(sp => sp.GetRequiredService<ClassificationTreeCapture>());
#endif

        return services;
    }
}
