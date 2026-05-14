// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// Legacy adapter that preserves <see cref="IPackageLockService"/> while delegating lock acquisition
/// to the <see cref="IPackageAccess"/> package boundary.
/// </summary>
public sealed class PackageLockFileService : IPackageLockService
{
    private readonly IPackageAccess _packageAccess;
    private readonly ILogger<PackageLockFileService> _logger;

    public PackageLockFileService(
        IPackageAccess packageAccess,
        ILogger<PackageLockFileService> logger)
    {
        _packageAccess = packageAccess ?? throw new ArgumentNullException(nameof(packageAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireAsync(string packagePath, string jobId, CancellationToken ct)
    {
        _logger.LogDebug("[PackageLock] Delegating package lock acquisition for job {JobId} through IPackageAccess.", jobId);
        _ = packagePath; // Kept for legacy interface compatibility.
        return await _packageAccess.AcquireLockAsync(jobId, ct).ConfigureAwait(false);
    }
}

