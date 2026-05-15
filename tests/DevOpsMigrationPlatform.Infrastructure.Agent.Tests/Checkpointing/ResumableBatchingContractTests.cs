// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Contract tests for the foundational types introduced by the resumable batching cursor feature.
/// Covers: BatchContinuationToken, ResumeDecision, ResumeRejectedException,
/// QueryFingerprintService, CheckpointingService (continuation token CRUD), PackagePathTestHelper.
/// </summary>
[TestClass]
public class ResumableBatchingContractTests
{
    // ── BatchContinuationToken ──────────────────────────────────────────

    [TestMethod]
    public void BatchContinuationToken_With_CreatesNewInstance()
    {
        var original = new BatchContinuationToken
        {
            ChangedDateUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            WorkItemId = 100,
            QueryFingerprint = "abc"
        };

        var completed = original with { Completed = true };

        Assert.IsFalse(original.Completed);
        Assert.IsTrue(completed.Completed);
        Assert.AreEqual(100, completed.WorkItemId);
    }

    // ── ResumeDecision ──────────────────────────────────────────────────

    [TestMethod]
    public void ResumeDecision_Rejected_CarriesFingerprints()
    {
        var decision = new ResumeDecision
        {
            Status = ResumeDecisionStatus.RejectedQueryMismatch,
            SavedQueryFingerprint = "aaa",
            CurrentQueryFingerprint = "bbb"
        };

        Assert.AreEqual("aaa", decision.SavedQueryFingerprint);
        Assert.AreEqual("bbb", decision.CurrentQueryFingerprint);
    }

    // ── ResumeRejectedException ─────────────────────────────────────────

    [TestMethod]
    public void ResumeRejectedException_CarriesDecision()
    {
        var decision = new ResumeDecision
        {
            Status = ResumeDecisionStatus.RejectedQueryMismatch,
            Reason = "incompatible_query"
        };

        var ex = new ResumeRejectedException(decision);

        Assert.AreSame(decision, ex.Decision);
        Assert.IsTrue(ex.Message.Contains("RejectedQueryMismatch"));
    }

    [TestMethod]
    public void ResumeRejectedException_NullDecision_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new ResumeRejectedException(null!));
    }

    // ── QueryFingerprintService ─────────────────────────────────────────

    [TestMethod]
    public void Compute_SameQuery_ProducesSameFingerprint()
    {
        var sut = new QueryFingerprintService();
        var fp1 = sut.Compute("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Foo'");
        var fp2 = sut.Compute("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Foo'");
        Assert.AreEqual(fp1, fp2);
    }

    [TestMethod]
    public void Compute_DifferentCasing_SameFingerprint()
    {
        var sut = new QueryFingerprintService();
        var fp1 = sut.Compute("select [System.Id] from WorkItems where [System.TeamProject] = 'Foo'");
        var fp2 = sut.Compute("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Foo'");
        Assert.AreEqual(fp1, fp2);
    }

    [TestMethod]
    public void Compute_DifferentWhitespace_SameFingerprint()
    {
        var sut = new QueryFingerprintService();
        var fp1 = sut.Compute("SELECT  [System.Id]   FROM   WorkItems");
        var fp2 = sut.Compute("SELECT [System.Id] FROM WorkItems");
        Assert.AreEqual(fp1, fp2);
    }

    [TestMethod]
    public void Compute_DifferentQuery_DifferentFingerprint()
    {
        var sut = new QueryFingerprintService();
        var fp1 = sut.Compute("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'A'");
        var fp2 = sut.Compute("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'B'");
        Assert.AreNotEqual(fp1, fp2);
    }

    [TestMethod]
    public void Compute_WithParameters_IncludedInFingerprint()
    {
        var sut = new QueryFingerprintService();
        var fp1 = sut.Compute("SELECT [System.Id] FROM WorkItems",
            new Dictionary<string, string> { ["project"] = "A" });
        var fp2 = sut.Compute("SELECT [System.Id] FROM WorkItems",
            new Dictionary<string, string> { ["project"] = "B" });
        Assert.AreNotEqual(fp1, fp2);
    }

    [TestMethod]
    public void Compute_ParameterOrder_DoesNotAffectFingerprint()
    {
        var sut = new QueryFingerprintService();
        var fp1 = sut.Compute("SELECT [System.Id] FROM WorkItems",
            new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        var fp2 = sut.Compute("SELECT [System.Id] FROM WorkItems",
            new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        Assert.AreEqual(fp1, fp2);
    }

    [TestMethod]
    public void Compute_EmptyQuery_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new QueryFingerprintService().Compute(""));
    }

    [TestMethod]
    public void Compute_ReturnsLowercaseHex()
    {
        var sut = new QueryFingerprintService();
        var fp = sut.Compute("SELECT [System.Id] FROM WorkItems");
        Assert.AreEqual(fp, fp.ToLowerInvariant());
        Assert.AreEqual(64, fp.Length); // SHA-256 = 32 bytes = 64 hex chars
    }

    // ── CheckpointingService: Continuation Token CRUD ───────────────────

    [TestMethod]
    public async Task ReadContinuationTokenAsync_NoToken_ReturnsNull()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "inventory" && c.Module == "inventory"),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/inventory.inventory.continuation.json", null)));

        var sut = new CheckpointingService(
            BuildEndpointAccessor().Object,
            package: package.Object);
        var result = await sut.ReadContinuationTokenAsync("inventory.inventory", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteThenRead_ContinuationToken_RoundTrips()
    {
        var package = PackageTestFactory.CreateLooseMock().Object;
        var sut = new CheckpointingService(
            BuildEndpointAccessor().Object,
            package: package);
        var token = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            WorkItemId = 42,
            QueryFingerprint = "abc123",
            GeneratedAtUtc = new DateTime(2024, 6, 15, 12, 0, 1, DateTimeKind.Utc),
            Completed = false
        };

        await sut.WriteContinuationTokenAsync("inventory.inventory", token, CancellationToken.None);
        var result = await sut.ReadContinuationTokenAsync("inventory.inventory", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result!.WorkItemId);
        Assert.AreEqual("abc123", result.QueryFingerprint);
        Assert.IsFalse(result.Completed);
    }

    [TestMethod]
    public async Task DeleteContinuationTokenAsync_CallsStateStoreDelete()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.ResetMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "inventory" && c.Module == "inventory"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(
            BuildEndpointAccessor().Object,
            package: package.Object);
        await sut.DeleteContinuationTokenAsync("inventory.inventory", CancellationToken.None);

        package.VerifyAll();
    }

    // ── PackagePathTestHelper ────────────────────────────────────────────────────

    [TestMethod]
    public void ContinuationFile_ReturnsExpectedPath()
    {
        var path = PackagePathTestHelper.ContinuationFile("Inventory");
        Assert.AreEqual(".migration/Checkpoints/inventory.continuation.json", path);
    }

    [TestMethod]
    public void ContinuationFile_LowercasesModuleName()
    {
        var path = PackagePathTestHelper.ContinuationFile("workitems");
        Assert.IsTrue(path.Contains("workitems.continuation.json"));
    }

    private static Mock<ICurrentJobEndpointAccessor> BuildEndpointAccessor()
    {
        var source = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        source.SetupGet(s => s.Url).Returns("https://dev.azure.com/contoso");
        source.SetupGet(s => s.Project).Returns("Shop");
        source.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");

        var accessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        accessor.SetupGet(a => a.Source).Returns(source.Object);
        accessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);
        return accessor;
    }
}
