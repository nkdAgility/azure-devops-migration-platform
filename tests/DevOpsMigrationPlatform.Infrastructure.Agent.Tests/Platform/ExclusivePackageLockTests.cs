// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

[TestClass]
public class ExclusivePackageLockTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Guid DeterministicGuid(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    private ActivePackageAccess BuildService(Guid agentInstanceId, Mock<IControlPlaneAgentClient>? mockControlPlane = null)
    {
        var state = new ActivePackageState
        {
            CurrentPackageUri = $"file:///{_tempDir.Replace(Path.DirectorySeparatorChar, '/')}",
            CurrentJob = new Job { JobId = "lock-test-job" }
        };

        return new ActivePackageAccess(
            state,
            new PackagePathRouter(),
            mockControlPlane?.Object,
            new AgentInstanceIdHolder(agentInstanceId),
            NullLogger<ActivePackageAccess>.Instance);
    }

    private string LockFilePath => Path.Combine(_tempDir, ".migration", "Checkpoints", "agent.lock");

    // ── Scenario 1: Second agent is hard-bounced when live lock exists ─────────

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task AcquireLockAsync_WhenLiveLockExists_SecondAgentReceivesPackageLockConflictException()
    {
        // Arrange: first agent acquires the lock
        var firstAgentGuid = DeterministicGuid("agent-001");
        var mockControlPlane = new Mock<IControlPlaneAgentClient>(MockBehavior.Strict);
        mockControlPlane
            .Setup(c => c.IsAgentActiveAsync(firstAgentGuid.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var firstAgent = BuildService(firstAgentGuid, mockControlPlane);
        using var lockHandle = await firstAgent.AcquireLockAsync("job-first", CancellationToken.None);

        // Act: second agent attempts to acquire
        var secondAgent = BuildService(Guid.NewGuid(), mockControlPlane);
        PackageLockConflictException? capturedException = null;
        try
        {
            var handle = await secondAgent.AcquireLockAsync("job-second", CancellationToken.None);
            handle.Dispose();
        }
        catch (PackageLockConflictException ex)
        {
            capturedException = ex;
        }

        // Assert
        Assert.IsNotNull(capturedException, "Expected PackageLockConflictException but none was thrown.");
        Assert.AreEqual(firstAgentGuid.ToString(), capturedException.OwnerAgentInstanceId,
            "Exception should report the first agent's instance ID as owner.");
    }

    // ── Scenario 2: Stale lock is replaced and agent proceeds normally ─────────

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task AcquireLockAsync_WhenStaleLockExists_StaleLockReplacedAndNewAgentAcquires()
    {
        // Arrange: write a stale lock file
        var staleAgentGuid = DeterministicGuid("agent-stale");
        var checkpointsDir = Path.Combine(_tempDir, ".migration", "Checkpoints");
        Directory.CreateDirectory(checkpointsDir);
        var lockContent = JsonSerializer.Serialize(new
        {
            jobId = "job-stale",
            agentInstanceId = staleAgentGuid.ToString(),
            acquiredAt = DateTimeOffset.UtcNow.AddHours(-2).ToString("O")
        });
        File.WriteAllText(Path.Combine(checkpointsDir, "agent.lock"), lockContent);

        var mockControlPlane = new Mock<IControlPlaneAgentClient>(MockBehavior.Strict);
        mockControlPlane
            .Setup(c => c.IsAgentActiveAsync(staleAgentGuid.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var newAgent = BuildService(Guid.NewGuid(), mockControlPlane);
        PackageLockConflictException? capturedException = null;
        IDisposable? lockHandle = null;
        try
        {
            lockHandle = await newAgent.AcquireLockAsync("job-new", CancellationToken.None);
        }
        catch (PackageLockConflictException ex)
        {
            capturedException = ex;
        }

        // Assert: stale lock was replaced
        Assert.IsNull(capturedException, "No PackageLockConflictException should have been thrown.");
        Assert.IsNotNull(lockHandle, "Expected a valid lock handle.");

        var lockFilePath = LockFilePath;
        Assert.IsTrue(File.Exists(lockFilePath), "Lock file should exist (newly acquired).");

        var newContent = File.ReadAllText(lockFilePath);
        Assert.IsFalse(newContent.Contains(staleAgentGuid.ToString()),
            "Lock file should no longer reference the stale agent.");

        lockHandle?.Dispose();
    }

    // ── Scenario 3: Lock is released when job completes ───────────────────────

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task AcquireLockAsync_WhenDisposed_LockFileNoLongerExists()
    {
        // Arrange
        var agentGuid = DeterministicGuid("agent-001");
        var service = BuildService(agentGuid);
        var lockHandle = await service.AcquireLockAsync("job-001", CancellationToken.None);

        Assert.IsTrue(File.Exists(LockFilePath), "Lock file should exist after acquire.");

        // Act
        lockHandle.Dispose();

        // Assert
        Assert.IsFalse(File.Exists(LockFilePath), "Lock file should have been deleted on dispose.");
    }
}
