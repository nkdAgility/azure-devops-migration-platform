// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

/// <summary>
/// Shared scenario state for Exclusive Package Lock step definitions.
/// Uses the real <see cref="PackageLockFileService"/> against a temp directory.
/// </summary>
public class ExclusivePackageLockContext : IDisposable
{
    public string TempDir { get; }
    public Mock<IControlPlaneAgentClient> MockControlPlane { get; } = new(MockBehavior.Strict);

    /// <summary>The lock handle returned by a successful AcquireAsync call.</summary>
    public IAsyncDisposable? LockHandle { get; set; }

    /// <summary>Exception captured when a second agent tries to acquire.</summary>
    public PackageLockConflictException? CapturedException { get; set; }

    /// <summary>Tracks whether the second acquisition succeeded without exception.</summary>
    public bool SecondAcquireSucceeded { get; set; }

    public ExclusivePackageLockContext()
    {
        TempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(TempDir);
    }

    public PackageLockFileService BuildService(Guid agentInstanceId)
        => new(agentInstanceId, MockControlPlane.Object, NullLogger<PackageLockFileService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }
}
