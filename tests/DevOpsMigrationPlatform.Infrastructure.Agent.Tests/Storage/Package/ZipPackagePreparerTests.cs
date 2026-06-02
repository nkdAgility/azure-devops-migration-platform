// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class ZipPackagePreparerTests
{
    [TestMethod]
    public async Task PrepareForImportAsync_TraversalEntry_ThrowsValidationException()
    {
        var packageRoot = CreateTempDirectory();
        var zipPath = Path.Combine(CreateTempDirectory(), "fixture.zip");
        var escapeTarget = Path.GetFullPath(Path.Combine(packageRoot, "..", "outside.txt"));

        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../outside.txt");
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync("escape");
            }

            var state = new ActivePackageState
            {
                CurrentPackageUri = packageRoot,
                CurrentJob = new Job { JobId = "job-1", Kind = JobKind.Import }
            };

            var sut = new ZipPackagePreparer(
                new FileSystemPackageStoreFactory(),
                state,
                NullLogger<ZipPackagePreparer>.Instance);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["MigrationPlatform:Package:PackagePath"] = zipPath
                    })
                .Build();

            var ex = await Assert.ThrowsExactlyAsync<PackageValidationException>(
                async () => await sut.PrepareForImportAsync(configuration, CancellationToken.None));

            Assert.AreEqual("PKG_ARCHIVE_ENTRY_INVALID", ex.Code);
            Assert.IsFalse(File.Exists(escapeTarget), "Fixture extraction must not write outside the package root.");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(zipPath)!);
            TryDeleteDirectory(packageRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
