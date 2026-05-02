#if !NET481
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

        services.AddSingleton<ITeamsOrchestrator, TeamsOrchestrator>();
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
        services.AddTransient<TeamImportOrchestrator>();
        services.AddSingleton<TeamSlugGenerator>();

        return services;
    }
}
#endif
