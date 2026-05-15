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
/// filesystem and streams every entry into the target <see cref="IPackageAccess"/> via
/// <see cref="IPackageAccess.PersistContentStreamAsync"/>.
///
/// <para>
/// The destination writes are storage-agnostic — this implementation works with both
/// <see cref="FileSystemArtefactStore"/> and a future <c>AzureBlobArtefactStore</c>.
/// </para>
/// </summary>
internal sealed class ZipPackagePreparer : IPackagePreparer
{
    private readonly ILogger<ZipPackagePreparer> _logger;

    public ZipPackagePreparer(ILogger<ZipPackagePreparer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PrepareForImportAsync(
        IPackageAccess packageAccess,
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

        int count = 0;
        using var archive = ZipFile.OpenRead(resolvedZipPath);
        foreach (var entry in archive.Entries)
        {
            // Skip directory entries — FullName ends with '/' per ZIP specification.
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            using var entryStream = entry.Open();
            await packageAccess.PersistContentStreamAsync(
                new PackageContentContext(PackageContentKind.Artefact, Address: new ZipEntryAddress(entry.FullName)),
                entryStream,
                contentType: null,
                cancellationToken).ConfigureAwait(false);
            count++;
        }

        _logger.LogInformation(
            "Extracted {Count} files from fixture {ZipPath} into package store.", count, resolvedZipPath);
    }

    private sealed class ZipEntryAddress(string fullName) : IPackageContentAddress
    {
        public string RelativePath => fullName;
    }
}
