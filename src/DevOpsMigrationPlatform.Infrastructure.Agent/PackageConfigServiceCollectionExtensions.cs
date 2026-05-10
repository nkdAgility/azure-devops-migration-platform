// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Registers package management services.
/// Call from every agent host that needs package store, preparer, and config store.
/// </summary>
public static class PackageConfigServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPackageStoreFactory"/>, <see cref="IPackagePreparer"/>,
    /// and <see cref="IPackageConfigStore"/> as a cohesive group.
    /// </summary>
    public static IServiceCollection AddPackageManagementServices(this IServiceCollection services)
    {
        services.AddPackageStorageServices();
        services.AddPackageBoundaryServices();
        services.AddSingleton<IPackageConfigStore, PackageConfigStore>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="PackageConfigStore"/> as the singleton <see cref="IPackageConfigStore"/>.
    /// Also registers <see cref="IPackageStoreFactory"/> if not already registered.
    /// Prefer <see cref="AddPackageManagementServices"/> when the full package stack is needed.
    /// </summary>
    public static IServiceCollection AddPackageConfigStore(this IServiceCollection services)
    {
        services.AddPackageStorageServices();
        services.AddPackageBoundaryServices();
        services.AddSingleton<IPackageConfigStore, PackageConfigStore>();
        return services;
    }
}
