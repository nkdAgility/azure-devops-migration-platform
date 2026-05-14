// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Acquires an exclusive lock on a migration package directory.
/// Guards against two agents running concurrently against the same package.
/// </summary>
public interface IPackageLockService
{
    /// <summary>
    /// Acquires an exclusive lock on <paramref name="packagePath"/>.
    /// Returns an <see cref="IAsyncDisposable"/> that releases the lock on dispose.
    /// </summary>
    /// <exception cref="PackageLockConflictException">
    /// Thrown when a live lock already exists (owning agent instance is still active).
    /// The second agent's job must be failed/bounced by the caller.
    /// </exception>
    Task<IDisposable> AcquireAsync(string packagePath, string jobId, CancellationToken ct);
}
