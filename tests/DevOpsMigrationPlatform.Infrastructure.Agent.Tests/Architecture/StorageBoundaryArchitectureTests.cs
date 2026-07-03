// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Architecture;

/// <summary>
/// Contract compatibility tests for ADR-0022 (MM-C1, MM-H1, CA-C2):
/// host composition roots own storage-implementation selection; modules and
/// job workers depend only on <c>Abstractions.Storage</c> contracts.
/// </summary>
[TestClass]
public sealed class StorageBoundaryArchitectureTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TfsObjectModelModule_HasNoProjectReferenceToStorageFileSystem()
    {
        // MM-C1: the Infrastructure.TfsObjectModel module must depend only on
        // Abstractions.Storage contracts; concrete storage selection belongs to hosts.
        var csprojPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "DevOpsMigrationPlatform.Infrastructure.TfsObjectModel",
            "DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.csproj");

        Assert.IsTrue(File.Exists(csprojPath), $"Expected csproj at {csprojPath}.");

        var content = File.ReadAllText(csprojPath);
        Assert.IsFalse(
            content.Contains("Infrastructure.Storage.FileSystem", StringComparison.OrdinalIgnoreCase),
            "Infrastructure.TfsObjectModel must not reference Infrastructure.Storage.FileSystem (MM-C1 / ADR-0022).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TfsObjectModelModule_DoesNotContainHostCompositionRoot()
    {
        // MM-H1: a module must not own a host composition root. The TFS subprocess
        // host builder (MigrationPlatformHost) lives in the TfsMigrationAgent host project.
        var moduleRoot = Path.Combine(
            FindRepoRoot(), "src", "DevOpsMigrationPlatform.Infrastructure.TfsObjectModel");

        var hostFiles = Directory
            .EnumerateFiles(moduleRoot, "MigrationPlatformHost.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.AreEqual(
            0,
            hostFiles.Count,
            $"Infrastructure.TfsObjectModel must not contain a host composition root (MM-H1 / ADR-0022). Found: {string.Join(", ", hostFiles)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void JobWorkers_DoNotUseStorageFileSystemNamespace()
    {
        // CA-C2: job workers consume storage exclusively through Abstractions.Storage
        // contracts (IPackageAccess, IPackageMigrationConfigLoader, ...); concrete
        // FileSystem types are selected only in the host composition roots.
        var repoRoot = FindRepoRoot();
        var workerFiles = new[]
        {
            Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.MigrationAgent", "JobAgentWorker.cs"),
            Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.TfsMigrationAgent", "TfsJobAgentWorker.cs"),
        };

        foreach (var workerFile in workerFiles)
        {
            Assert.IsTrue(File.Exists(workerFile), $"Expected worker source at {workerFile}.");
            var content = File.ReadAllText(workerFile);
            Assert.IsFalse(
                content.Contains("Infrastructure.Storage.FileSystem", StringComparison.OrdinalIgnoreCase),
                $"{Path.GetFileName(workerFile)} must not use Infrastructure.Storage.FileSystem directly (CA-C2 / ADR-0022).");
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevOpsMigrationPlatform.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root walking up from {AppContext.BaseDirectory}.");
    }
}
