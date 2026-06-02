// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;

/// <summary>
/// <see cref="IPackagePreparer"/> implementation that reads a fixture ZIP from the local
/// filesystem and streams every entry directly into the injected <see cref="IArtefactStore"/>.
///
/// <para>
/// Writes bypass the typed routing surface (<see cref="IPackageAccess"/>) because a fixture
/// ZIP is an already-valid package: every entry path is already correctly scoped at the
/// source. Writing directly to the store preserves the archive structure verbatim.
/// </para>
/// </summary>
internal sealed class ZipPackagePreparer : IPackagePreparer
{
    private readonly IPackageStoreFactory _packageStoreFactory;
    private readonly ActivePackageState _packageState;
    private readonly ILogger<ZipPackagePreparer> _logger;

    public ZipPackagePreparer(
        IPackageStoreFactory packageStoreFactory,
        ActivePackageState packageState,
        ILogger<ZipPackagePreparer> logger)
    {
        _packageStoreFactory = packageStoreFactory;
        _packageState = packageState;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PrepareForImportAsync(
        IConfiguration packageConfig,
        CancellationToken cancellationToken)
    {
        var zipPath = packageConfig.GetSection("MigrationPlatform:Package:PackagePath").Value;
        if (string.IsNullOrWhiteSpace(zipPath))
            return;

        var resolvedZipPath = Path.GetFullPath(zipPath);
        if (!File.Exists(resolvedZipPath))
        {
            _logger.LogWarning(
                "PackagePath '{ZipPath}' not found — skipping fixture extraction.",
                resolvedZipPath);
            return;
        }

        _logger.LogInformation("Extracting package fixture {ZipPath} into package store.", resolvedZipPath);

        var packageUri = _packageState.CurrentPackageUri;
        if (string.IsNullOrWhiteSpace(packageUri))
        {
            throw new InvalidOperationException(
                "Active package URI is not set. Package fixtures can only be prepared within an active leased job.");
        }

        var (store, _) = _packageStoreFactory.Create(packageUri!);
        int count = 0;
        using var archive = ZipFile.OpenRead(resolvedZipPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            using var entryStream = entry.Open();
            await store.WriteStreamAsync(entry.FullName, entryStream, cancellationToken).ConfigureAwait(false);
            count++;
        }

        _logger.LogInformation(
            "Extracted {Count} files from fixture {ZipPath} into package store.", count, resolvedZipPath);
    }
}
