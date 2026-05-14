// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Prepares a package for import by materialising any external fixture referenced in
/// <c>MigrationPlatform:Package:PackagePath</c> into the target <see cref="IArtefactStore"/>.
///
/// <para>
/// This abstraction decouples fixture extraction from the job worker and allows future
/// implementations (e.g. Azure Blob Storage) to stream entries without any filesystem coupling.
/// All writes go through <see cref="IArtefactStore.WriteStreamAsync"/> so the destination is
/// storage-backend agnostic.
/// </para>
/// </summary>
public interface IPackagePreparer
{
    /// <summary>
    /// Inspects <paramref name="packageConfig"/> for a fixture path and, if present,
    /// streams all entries from the fixture into <paramref name="artefactStore"/>.
    /// If no <c>PackagePath</c> is configured, this method is a no-op.
    /// </summary>
    /// <param name="artefactStore">The destination store for the extracted entries.</param>
    /// <param name="packageConfig">
    /// The loaded <c>migration-config.json</c> configuration.
    /// The value at <c>MigrationPlatform:Package:PackagePath</c> is the source fixture path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PrepareForImportAsync(
        IArtefactStore artefactStore,
        IConfiguration packageConfig,
        CancellationToken cancellationToken);
}
