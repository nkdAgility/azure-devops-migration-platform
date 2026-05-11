// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Contract tests for the foundational types introduced by the resumable batching cursor feature.
/// Covers: BatchContinuationToken, ResumeDecision, ResumeRejectedException,
/// QueryFingerprintService, CheckpointingService (continuation token CRUD), PackagePaths.
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
        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);
        stateStore.Setup(s => s.ReadAsync(
                PackagePaths.ContinuationFile("inventory"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = new CheckpointingService(
            stateStore.Object,
            package: PackageTestFactory.CreateStateDelegatingMock(stateStore.Object).Object);
        var result = await sut.ReadContinuationTokenAsync("inventory", CancellationToken.None);

        Assert.IsNull(result);
        stateStore.VerifyAll();
    }

    [TestMethod]
    public async Task WriteThenRead_ContinuationToken_RoundTrips()
    {
        var store = new Dictionary<string, string>();
        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);

        stateStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((k, v, _) => store[k] = v)
            .Returns(Task.CompletedTask);

        stateStore.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => store.GetValueOrDefault(key));

        var sut = new CheckpointingService(
            stateStore.Object,
            package: PackageTestFactory.CreateStateDelegatingMock(stateStore.Object).Object);
        var token = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            WorkItemId = 42,
            QueryFingerprint = "abc123",
            GeneratedAtUtc = new DateTime(2024, 6, 15, 12, 0, 1, DateTimeKind.Utc),
            Completed = false
        };

        await sut.WriteContinuationTokenAsync("inventory", token, CancellationToken.None);
        var result = await sut.ReadContinuationTokenAsync("inventory", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result!.WorkItemId);
        Assert.AreEqual("abc123", result.QueryFingerprint);
        Assert.IsFalse(result.Completed);
    }

    [TestMethod]
    public async Task DeleteContinuationTokenAsync_CallsStateStoreDelete()
    {
        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);
        stateStore.Setup(s => s.DeleteAsync(
                PackagePaths.ContinuationFile("inventory"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(
            stateStore.Object,
            package: PackageTestFactory.CreateStateDelegatingMock(stateStore.Object).Object);
        await sut.DeleteContinuationTokenAsync("inventory", CancellationToken.None);

        stateStore.VerifyAll();
    }

    // ── PackagePaths ────────────────────────────────────────────────────

    [TestMethod]
    public void ContinuationFile_ReturnsExpectedPath()
    {
        var path = PackagePaths.ContinuationFile("Inventory");
        Assert.AreEqual(".migration/Checkpoints/inventory.continuation.json", path);
    }

    [TestMethod]
    public void ContinuationFile_LowercasesModuleName()
    {
        var path = PackagePaths.ContinuationFile("workitems");
        Assert.IsTrue(path.Contains("workitems.continuation.json"));
    }
}
