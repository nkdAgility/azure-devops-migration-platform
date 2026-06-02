// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Prepares a package for import by materialising any external fixture referenced in
/// <c>MigrationPlatform:Package:PackagePath</c> into the package store.
///
/// <para>
/// Implementations take their storage dependency at construction time. The interface
/// exposes only configuration and cancellation — callers do not need to know whether
/// the underlying store is filesystem, Azure Blob, or otherwise.
/// </para>
/// </summary>
public interface IPackagePreparer
{
    /// <summary>
    /// Inspects <paramref name="packageConfig"/> for a fixture path and, if present,
    /// extracts all entries from the fixture into the underlying store.
    /// If no <c>PackagePath</c> is configured, this method is a no-op.
    /// </summary>
    Task PrepareForImportAsync(
        IConfiguration packageConfig,
        CancellationToken cancellationToken);
}
