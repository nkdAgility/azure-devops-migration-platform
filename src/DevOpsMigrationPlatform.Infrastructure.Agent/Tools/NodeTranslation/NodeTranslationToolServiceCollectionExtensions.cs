// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
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
    /// (e.g. <c>AddAzureDevOpsWorkItemImport</c>, <c>AddSimulatedWorkItemImport</c>,
    /// <c>TfsClassificationTreeReader</c>).
    /// </para>
    /// <para>
    /// Tools are registered as <b>Scoped</b> so that <see cref="IOptionsSnapshot{TOptions}"/>
    /// is recomputed from <see cref="ActiveJobConfigState.PackageConfig"/> on every per-job
    /// DI scope.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNodeTranslationToolServices(this IServiceCollection services)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<NodeTranslationOptions>("Classification node path translation configuration");
#endif

        // Bind from per-job PackageConfig via ActiveJobConfigState (set before job scope is created).
        services.AddOptions<NodeTranslationOptions>()
            .Configure<IJobConfiguration>((opts, state) =>
            {
                state.PackageConfig?.GetSection(NodeTranslationOptions.SectionName).Bind(opts);
            });

#if !NET481
        services.AddSingleton<IValidateOptions<NodeTranslationOptions>, NodeTranslationOptionsValidator>();
        // Scoped so IOptionsSnapshot<NodeTranslationOptions> is resolved per-job scope.
        services.AddScoped<NodeTranslationTool>(sp => new NodeTranslationTool(
            sp.GetRequiredService<IOptionsSnapshot<NodeTranslationOptions>>(),
            sp.GetRequiredService<ILogger<NodeTranslationTool>>(),
            sp.GetService<IPlatformMetrics>()));
        services.AddScoped<INodeTranslationTool>(sp => sp.GetRequiredService<NodeTranslationTool>());
        services.AddScoped<INodeTranslationValidator>(sp => new NodeTranslationValidator(
            sp.GetRequiredService<IOptionsSnapshot<NodeTranslationOptions>>(),
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
