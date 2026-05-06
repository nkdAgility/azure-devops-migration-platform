// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// DI registration for the field-transform tool and its supporting services.
/// </summary>
public static class FieldTransformToolServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IFieldTransformFactory"/>, <see cref="IFieldTransformTool"/>,
    /// and <see cref="IFieldTransformValidator"/> to the service collection.
    /// <para>
    /// Tools are registered as <b>Scoped</b> so that <see cref="IOptionsSnapshot{TOptions}"/>
    /// is recomputed from <see cref="ActiveJobConfigState.PackageConfig"/> on every per-job
    /// DI scope. This ensures options reflect the <c>migration-config.json</c> loaded for
    /// the current job, not the empty <c>appsettings.json</c> present at host startup.
    /// </para>
    /// </summary>
    public static IServiceCollection AddFieldTransformToolServices(this IServiceCollection services)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<FieldTransformOptions>("Field transformation configuration for work item import");
#endif

        // Bind from per-job PackageConfig (set on ActiveJobConfigState before the job scope is created).
        // IOptionsSnapshot<T> computes .Value once per scope, so each job scope gets options from
        // the migration-config.json that was loaded for that job.
        services.AddOptions<FieldTransformOptions>()
            .Configure<IJobConfiguration>((opts, state) =>
            {
                state.PackageConfig?.GetSection(FieldTransformOptions.SectionName).Bind(opts);
            });

#if !NET481
        services.AddSingleton<IValidateOptions<FieldTransformOptions>, FieldTransformOptionsValidator>();
#endif

        services.AddScoped<IFieldTransformFactory>(sp =>
            new FieldTransformFactory(sp.GetService<ILoggerFactory>()));
        // Scoped so IOptionsSnapshot<FieldTransformOptions> is resolved per-job scope,
        // giving each job the options from its own migration-config.json.
        services.AddScoped<IFieldTransformTool>(sp => new FieldTransformTool(
            sp.GetRequiredService<IOptionsSnapshot<FieldTransformOptions>>(),
            sp.GetRequiredService<IFieldTransformFactory>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetService<IPlatformMetrics>()));

        // IFieldDefinitionProviderFactory is optional — connectors register it when available.
        services.AddScoped<IFieldTransformValidator>(sp =>
            new FieldTransformValidator(
                sp.GetRequiredService<IOptionsSnapshot<FieldTransformOptions>>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<FieldTransformValidator>(),
                sp.GetService<IFieldDefinitionProviderFactory>()));

        return services;
    }
}
