// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;

/// <summary>
/// Registers package boundary and storage implementation services used by agent runtime paths.
/// </summary>
public static class PackageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed package boundary services.
    /// </summary>
    public static IServiceCollection AddPackageBoundaryServices(this IServiceCollection services)
    {
        services.TryAddSingleton<PackagePathRouter>();
        services.TryAddSingleton<IPackageAccess>(sp =>
            new ActivePackageAccess(
                sp.GetRequiredService<ActivePackageState>(),
                sp.GetRequiredService<PackagePathRouter>(),
                sp.GetService<DevOpsMigrationPlatform.Abstractions.ControlPlaneApi.IControlPlaneAgentClient>(),
                sp.GetService<AgentInstanceIdHolder>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<ActivePackageAccess>>()));
        return services;
    }

    /// <summary>
    /// Registers package storage and preparation implementations.
    /// </summary>
    public static IServiceCollection AddPackageStorageServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IPackageStoreFactory, FileSystemPackageStoreFactory>();
        services.TryAddSingleton<IPackagePreparer, ZipPackagePreparer>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IPackageMigrationConfigLoader"/>. Also registers boundary and storage services
    /// if not already registered. Prefer <see cref="AddPackageManagementServices"/> when the full stack is needed.
    /// </summary>
    public static IServiceCollection AddPackageMigrationConfigLoader(this IServiceCollection services)
    {
        services.AddPackageBoundaryServices();
        services.AddSingleton<IPackageMigrationConfigLoader, PackageMigrationConfigLoader>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IPackageStoreFactory"/>, <see cref="IPackagePreparer"/>,
    /// and <see cref="IPackageMigrationConfigLoader"/> as a cohesive group.
    /// </summary>
    public static IServiceCollection AddPackageManagementServices(this IServiceCollection services)
    {
        services.AddPackageBoundaryServices();
        services.AddSingleton<IPackageMigrationConfigLoader, PackageMigrationConfigLoader>();
        return services;
    }
}
