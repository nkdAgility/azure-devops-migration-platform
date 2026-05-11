// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;

public static class IdentityLookupToolServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityLookupToolServices(this IServiceCollection services)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<IdentityLookupOptions>("Identity mapping and resolution configuration");
#endif

        services.TryAddSingleton<ICurrentPackageConfigAccessor, CurrentPackageConfigAccessor>();

        // Bind from the explicit current package config set for the current job.
        // IOptionsSnapshot<T> computes .Value once per scope, giving per-job options.
        services.AddOptions<IdentityLookupOptions>()
            .Configure<ICurrentPackageConfigAccessor>((opts, state) =>
            {
                state.Current?.GetSection(IdentityLookupOptions.SectionName).Bind(opts);
            });
        // Scoped so IOptionsSnapshot<IdentityLookupOptions> is resolved per-job scope,
        // giving each job options from its own migration-config.json.
        services.AddScoped<IdentityLookupTool>(sp => new IdentityLookupTool(
            sp.GetRequiredService<IOptionsSnapshot<IdentityLookupOptions>>(),
            sp.GetService<ILogger<IdentityLookupTool>>(),
            sp.GetRequiredService<IPackageAccess>()));
        services.AddScoped<IIdentityLookupTool>(sp => sp.GetRequiredService<IdentityLookupTool>());
        return services;
    }
}
#endif
