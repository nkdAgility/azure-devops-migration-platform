// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

/// <summary>
/// HX-H1 / ADR-0025: <see cref="IPackageAccess.ResetMetaAsync"/> must expose a
/// storage-neutral error contract. Filesystem-specific exception types
/// (<see cref="System.IO.FileNotFoundException"/>) must never leak through the
/// package boundary; absence of the meta artefact is reported via the
/// abstraction-owned <c>PackageMetaNotFoundException</c> (or completes silently —
/// reset of a missing meta is not an error).
/// </summary>
[TestClass]
public sealed class PackageMetaResetErrorContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PackageMetaNotFoundException_IsDefinedInAbstractionsStorage()
    {
        var contractsAssembly = PackageBoundaryTestFixture.ContractsAssemblyName;
        var type = Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageMetaNotFoundException, {contractsAssembly}");
        Assert.IsNotNull(type, "PackageMetaNotFoundException must be owned by the Abstractions.Storage contract assembly.");
        Assert.IsTrue(typeof(Exception).IsAssignableFrom(type));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ResetMetaAsync_MissingMeta_DoesNotThrowFileSystemException()
    {
        var store = new InMemoryPackageAccess();
        var (sut, _) = ActivePackageTestFactory.Create(store, "job-reset", DevOpsMigrationPlatform.Abstractions.Jobs.JobKind.Export);

        // Resetting a meta artefact that was never written must be storage-neutral:
        // either it completes silently (idempotent delete) or throws the
        // abstraction-owned PackageMetaNotFoundException — never System.IO types.
        try
        {
            await sut.ResetMetaAsync(new PackageMetaContext(PackageMetaKind.ExecutionPlan), CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.IO.IOException)
        {
            Assert.Fail($"Filesystem exception type leaked through IPackageAccess.ResetMetaAsync: {ex.GetType().FullName}");
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PackageBoundaryConsumers_DoNotCatchFileSystemExceptionsFromResetMeta()
    {
        // Guard: the workers are IPackageAccess consumers; they must catch the
        // storage-neutral PackageMetaNotFoundException, not System.IO types.
        var repoRoot = FindRepoRoot();
        var workerPath = System.IO.Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.MigrationAgent", "JobAgentWorker.cs");
        var source = System.IO.File.ReadAllText(workerPath);
        StringAssert.DoesNotMatch(
            source,
            new System.Text.RegularExpressions.Regex(@"catch\s*\(\s*(System\.IO\.)?FileNotFoundException"),
            "JobAgentWorker must not catch filesystem-specific exceptions from the package boundary (HX-H1).");
    }

    private static string FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repository root.");
        return dir!.FullName;
    }
}
