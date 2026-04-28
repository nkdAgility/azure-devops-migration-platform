#if !NET481
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // Register the IdentityLookupTool — replaces the old IdentityMappingService
        // when the IdentitiesModule is active.
        services.AddIdentityLookupToolServices();

        return services;
    }
}
#endif
