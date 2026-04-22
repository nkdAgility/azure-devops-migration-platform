using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

[Binding]
[Scope(Feature = "Exclusive Package Lock")]
public class ExclusivePackageLockSteps
{
    private readonly ExclusivePackageLockContext _ctx;

    private PackageLockFileService? _firstAgentService;
    private PackageLockFileService? _secondAgentService;
    private string? _staleLockAgentId;

    public ExclusivePackageLockSteps(ExclusivePackageLockContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a migration package exists at a temporary directory")]
    public void GivenAMigrationPackageExistsAtATemporaryDirectory()
    {
        // TempDir is already created in the context constructor.
        Assert.IsTrue(Directory.Exists(_ctx.TempDir));
    }

    // ── Scenario 1: Second agent is hard-bounced ──────────────────────────────

    [Given(@"an agent with instance ID ""(.*)"" holds the lock on the package")]
    public async Task GivenAnAgentWithInstanceIdHoldsTheLock(string agentId)
    {
        var agentGuid = DeterministicGuid(agentId);
        _firstAgentService = _ctx.BuildService(agentGuid);
        _ctx.LockHandle = await _firstAgentService.AcquireAsync(_ctx.TempDir, "job-first", CancellationToken.None);
    }

    [Given(@"the ControlPlane reports agent ""(.*)"" as Active")]
    public void GivenControlPlaneReportsAgentAsActive(string agentId)
    {
        _ctx.MockControlPlane
            .Setup(c => c.IsAgentActiveAsync(DeterministicGuid(agentId).ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [When("a second agent attempts to acquire the lock on the package")]
    public async Task WhenASecondAgentAttemptsToAcquireTheLock()
    {
        var secondGuid = Guid.NewGuid();
        _secondAgentService = _ctx.BuildService(secondGuid);
        try
        {
            var handle = await _secondAgentService.AcquireAsync(_ctx.TempDir, "job-second", CancellationToken.None);
            await handle.DisposeAsync();
            _ctx.SecondAcquireSucceeded = true;
        }
        catch (PackageLockConflictException ex)
        {
            _ctx.CapturedException = ex;
        }
    }

    [Then("the second agent receives a PackageLockConflictException")]
    public void ThenSecondAgentReceivesPackageLockConflictException()
    {
        Assert.IsNotNull(_ctx.CapturedException, "Expected PackageLockConflictException but none was thrown.");
    }

    [Then(@"the exception reports owner agent instance ""(.*)""")]
    public void ThenExceptionReportsOwnerAgentInstance(string expectedAgentId)
    {
        Assert.IsNotNull(_ctx.CapturedException);
        Assert.AreEqual(
            DeterministicGuid(expectedAgentId).ToString(),
            _ctx.CapturedException.OwnerAgentInstanceId);
    }

    // ── Scenario 2: Stale lock replaced ───────────────────────────────────────

    [Given(@"a stale lock file exists in the package Checkpoints directory for agent ""(.*)""")]
    public void GivenStaleLockFileExistsForAgent(string agentId)
    {
        _staleLockAgentId = agentId;
        var checkpointsDir = Path.Combine(_ctx.TempDir, PackagePaths.SystemRoot, "Checkpoints");
        Directory.CreateDirectory(checkpointsDir);

        var lockContent = JsonSerializer.Serialize(new
        {
            jobId = "job-stale",
            agentInstanceId = DeterministicGuid(agentId).ToString(),
            acquiredAt = DateTimeOffset.UtcNow.AddHours(-2).ToString("O")
        });

        File.WriteAllText(Path.Combine(checkpointsDir, "agent.lock"), lockContent);
    }

    [Given(@"the ControlPlane reports agent ""(.*)"" as not found")]
    public void GivenControlPlaneReportsAgentAsNotFound(string agentId)
    {
        _ctx.MockControlPlane
            .Setup(c => c.IsAgentActiveAsync(DeterministicGuid(agentId).ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [When("an agent attempts to acquire the lock on the package")]
    public async Task WhenAnAgentAttemptsToAcquireTheLock()
    {
        var agentGuid = Guid.NewGuid();
        var service = _ctx.BuildService(agentGuid);
        try
        {
            _ctx.LockHandle = await service.AcquireAsync(_ctx.TempDir, "job-new", CancellationToken.None);
            _ctx.SecondAcquireSucceeded = true;
        }
        catch (PackageLockConflictException ex)
        {
            _ctx.CapturedException = ex;
        }
    }

    [Then("the stale lock is deleted")]
    public void ThenStaleLockIsDeleted()
    {
        // After acquiring the new lock, the stale content has been replaced.
        // The lock file now exists but belongs to the new agent, not the stale one.
        var lockFilePath = Path.Combine(_ctx.TempDir, PackagePaths.SystemRoot, "Checkpoints", "agent.lock");
        Assert.IsTrue(File.Exists(lockFilePath), "Lock file should exist (newly acquired).");

        var content = File.ReadAllText(lockFilePath);
        if (_staleLockAgentId is not null)
        {
            Assert.IsFalse(
                content.Contains(DeterministicGuid(_staleLockAgentId).ToString()),
                "Lock file should no longer reference the stale agent.");
        }
    }

    [Then("the new agent acquires the lock successfully")]
    public void ThenNewAgentAcquiresLockSuccessfully()
    {
        Assert.IsTrue(_ctx.SecondAcquireSucceeded, "Expected the new agent to acquire the lock.");
        Assert.IsNotNull(_ctx.LockHandle, "Expected a valid lock handle.");
    }

    [Then("no PackageLockConflictException is thrown")]
    public void ThenNoPackageLockConflictExceptionIsThrown()
    {
        Assert.IsNull(_ctx.CapturedException, "No PackageLockConflictException should have been thrown.");
    }

    // ── Scenario 3: Lock released on dispose ──────────────────────────────────

    [When("the job completes and the lock handle is disposed")]
    public async Task WhenJobCompletesAndLockHandleIsDisposed()
    {
        Assert.IsNotNull(_ctx.LockHandle, "Lock handle must exist to dispose.");
        await _ctx.LockHandle.DisposeAsync();
        _ctx.LockHandle = null;
    }

    [Then("the lock file no longer exists in the package Checkpoints directory")]
    public void ThenLockFileNoLongerExists()
    {
        var lockFilePath = Path.Combine(_ctx.TempDir, PackagePaths.SystemRoot, "Checkpoints", "agent.lock");
        Assert.IsFalse(File.Exists(lockFilePath), "Lock file should have been deleted on dispose.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a deterministic GUID from a string agent ID so control-plane mock
    /// setups match the agent instance ID written into the lock file.
    /// </summary>
    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    [TestCleanup]
    public void Cleanup() => _ctx.Dispose();
}
