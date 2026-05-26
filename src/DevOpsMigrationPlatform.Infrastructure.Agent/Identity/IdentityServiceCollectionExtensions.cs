// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;
#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Identity;

/// <summary>
/// Extension methods for registering identity services with the DI container.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IdentitiesModule"/> as an <see cref="IModule"/> implementation
    /// and binds <see cref="IdentitiesModuleOptions"/> from configuration.
    /// </summary>
    public static IServiceCollection AddIdentitiesModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<IdentitiesModuleOptions>("Identities export/import module configuration");
#endif

        services.AddSingleton<IIdentitiesOrchestrator, IdentitiesOrchestrator>();
        services.AddTransient<IModule, IdentitiesModule>();

        if (configuration is not null)
        {
            services.Configure<IdentitiesModuleOptions>(
                configuration.GetSection(IdentitiesModuleOptions.SectionName));
        }
        else
        {
            services.AddOptions<IdentitiesModuleOptions>();
        }

#if !NET481
        services.AddSingleton<IIdentityMappingService, IdentityMappingService>();
        // Register the IdentityLookupTool seam used by module import/export paths.
        services.AddIdentityLookupToolServices();
#else
        // On NET481 (TFS agent), use the pass-through implementation because identity lookup
        // is unavailable in this runtime path.
        services.TryAddSingleton<IIdentityMappingService, PassThroughIdentityMappingService>();
#endif

        return services;
    }
}
