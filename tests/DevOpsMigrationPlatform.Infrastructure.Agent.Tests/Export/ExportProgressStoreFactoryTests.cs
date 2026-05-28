// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[TestClass]
public class ExportProgressStoreFactoryTests
{
    [TestMethod]
    public async Task Create_WhenGivenResolvedDatabasePath_InitializesSqlite()
    {
        var rootName = Path.Combine("TestResults", Guid.NewGuid().ToString("N"));
        var relativePackageRoot = Path.Combine(rootName, "package-root");
        var expectedDbPath = Path.Combine(
            Path.GetFullPath(relativePackageRoot),
            PackagePathTestHelper.SystemRoot,
            "Checkpoints",
            "export_progress.db");
        IExportProgressStore? store = null;

        try
        {
            var sut = new ExportProgressStoreFactory();
            store = sut.Create(expectedDbPath);

            await store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(File.Exists(expectedDbPath), $"Expected SQLite DB at {expectedDbPath}");
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            var absoluteRoot = Path.GetFullPath(rootName);
            if (Directory.Exists(absoluteRoot))
            {
                DeleteDirectoryWithRetry(absoluteRoot);
            }
        }
    }

    [TestMethod]
    public async Task Create_WhenWindowsPathExceedsMaxPath_StillInitializesSqlite()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("This test requires Windows.");
        }

        var rootName = Path.Combine(
            "TestResults",
            new string('a', 80),
            new string('b', 80),
            Guid.NewGuid().ToString("N"));
        var relativePackageRoot = Path.Combine(rootName, "package-root");
        var expectedDbPath = Path.Combine(
            Path.GetFullPath(relativePackageRoot),
            PackagePathTestHelper.SystemRoot,
            "Checkpoints",
            "export_progress.db");
        IExportProgressStore? store = null;

        try
        {
            Assert.IsTrue(expectedDbPath.Length >= 260, $"Expected long path, got length {expectedDbPath.Length}");

            var sut = new ExportProgressStoreFactory();
            store = sut.Create(expectedDbPath);

            await store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(File.Exists(expectedDbPath), $"Expected SQLite DB at {expectedDbPath}");
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            var absoluteRoot = Path.GetFullPath(rootName);
            if (Directory.Exists(absoluteRoot))
            {
                DeleteDirectoryWithRetry(absoluteRoot);
            }
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}