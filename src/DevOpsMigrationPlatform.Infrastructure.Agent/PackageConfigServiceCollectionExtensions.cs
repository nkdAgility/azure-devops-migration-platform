using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Registers <see cref="IPackageConfigStore"/> and its dependencies.
/// Call from every agent host that needs to read or write <c>migration-config.json</c>.
/// </summary>
public static class PackageConfigServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PackageConfigStore"/> as the singleton <see cref="IPackageConfigStore"/>.
    /// </summary>
    public static IServiceCollection AddPackageConfigStore(this IServiceCollection services)
    {
        services.AddSingleton<IPackageConfigStore, PackageConfigStore>();
        return services;
    }
}
