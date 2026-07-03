// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityTranslation;

public static class IdentityTranslationToolServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityTranslationToolServices(this IServiceCollection services)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<IdentityTranslationOptions>("Identity mapping and resolution configuration");
#endif

        services.TryAddSingleton<ICurrentPackageConfigAccessor, CurrentPackageConfigAccessor>();

        // Bind from the explicit current package config set for the current job.
        // IOptionsSnapshot<T> computes .Value once per scope, giving per-job options.
        services.AddOptions<IdentityTranslationOptions>()
            .Configure<ICurrentPackageConfigAccessor>((opts, state) =>
            {
                state.Current?.GetSection(IdentityTranslationOptions.SectionName).Bind(opts);
            });
        // Singleton per the Tool contract: pure translation engine (ADR-0026, TC-M1).
        // Package I/O and map ownership live with IIdentitiesOrchestrator.
        services.AddSingleton<IdentityTranslationTool>(sp => new IdentityTranslationTool(
            sp.GetRequiredService<IOptions<IdentityTranslationOptions>>(),
            sp.GetService<ILogger<IdentityTranslationTool>>()));
        services.AddSingleton<IIdentityTranslationTool>(sp => sp.GetRequiredService<IdentityTranslationTool>());
        return services;
    }
}
