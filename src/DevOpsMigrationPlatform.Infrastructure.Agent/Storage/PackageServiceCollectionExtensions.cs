// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

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
        services.TryAddSingleton<IPackageAccess, ActivePackageAccess>();
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
}
