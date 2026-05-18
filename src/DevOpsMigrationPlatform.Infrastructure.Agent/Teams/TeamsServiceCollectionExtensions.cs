// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Extension methods for registering teams services with the DI container.
/// </summary>
public static class TeamsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TeamsModule"/> as an <see cref="IModule"/> implementation
    /// and all supporting services (orchestrators, slug generator, options).
    /// </summary>
    public static IServiceCollection AddTeamsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<TeamsModuleOptions>("Teams export/import module configuration");
#endif

        // Scoped (not Singleton) so each per-job DI scope gets its own TeamsOrchestrator
        // instance and — via TeamsOrchestrator → TeamExportOrchestrator — its own
        // IReferencedPathTracker.  The T012 invariant requires every component within a
        // single job scope to share the same ReferencedPathTracker so the internal
        // SemaphoreSlim correctly serialises concurrent file writes to
        // Nodes/referenced-paths.json.  A Singleton TeamsOrchestrator would capture the
        // root-scope IReferencedPathTracker (a different instance from the per-job one
        // used by WorkItemsModule), breaking that coordination and causing a sharing-
        // violation IOException under concurrent export.
        services.AddScoped<ITeamsOrchestrator, TeamsOrchestrator>();
        services.AddTransient<IModule, TeamsModule>();

        if (configuration is not null)
        {
            services.Configure<TeamsModuleOptions>(
                configuration.GetSection(TeamsModuleOptions.SectionName));
        }
        else
        {
            services.AddOptions<TeamsModuleOptions>();
        }

        services.AddTransient<TeamExportOrchestrator>();
#if !NET481
        services.AddTransient<TeamImportOrchestrator>();
#endif
        services.AddSingleton<TeamSlugGenerator>();

        return services;
    }
}
