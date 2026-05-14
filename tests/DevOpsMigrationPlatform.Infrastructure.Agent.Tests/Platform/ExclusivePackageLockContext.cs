// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

/// <summary>
/// Shared scenario state for Exclusive Package Lock step definitions.
/// Uses the real <see cref="ActivePackageAccess"/> against a temp directory-backed package URI.
/// </summary>
public class ExclusivePackageLockContext : IDisposable
{
    public string TempDir { get; }
    public Mock<IControlPlaneAgentClient> MockControlPlane { get; } = new(MockBehavior.Strict);

    /// <summary>The lock handle returned by a successful AcquireAsync call.</summary>
    public IDisposable? LockHandle { get; set; }

    /// <summary>Exception captured when a second agent tries to acquire.</summary>
    public PackageLockConflictException? CapturedException { get; set; }

    /// <summary>Tracks whether the second acquisition succeeded without exception.</summary>
    public bool SecondAcquireSucceeded { get; set; }

    public ExclusivePackageLockContext()
    {
        TempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(TempDir);
    }

    internal ActivePackageAccess BuildService(Guid agentInstanceId)
    {
        var state = new ActivePackageState
        {
            CurrentJob = new Job
            {
                JobId = "lock-test-job",
                Package = new JobPackage { PackageUri = $"file:///{TempDir.Replace(Path.DirectorySeparatorChar, '/')}" }
            }
        };

        return new ActivePackageAccess(
            state,
            new PackagePathRouter(),
            MockControlPlane.Object,
            new AgentInstanceIdHolder(agentInstanceId),
            NullLogger<ActivePackageAccess>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }
}
