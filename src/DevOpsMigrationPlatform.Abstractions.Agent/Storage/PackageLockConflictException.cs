using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Thrown when an agent attempts to acquire a lock on a package that is already held
/// by a live agent instance. The second agent must fail/bounce immediately.
/// </summary>
public sealed class PackageLockConflictException : Exception
{
    /// <summary>Absolute path to the package directory that is locked.</summary>
    public string PackagePath { get; }

    /// <summary>Job ID of the agent currently holding the lock.</summary>
    public string OwnerJobId { get; }

    /// <summary>Agent instance GUID of the agent currently holding the lock.</summary>
    public string OwnerAgentInstanceId { get; }

    /// <summary>UTC timestamp when the owning agent acquired the lock.</summary>
    public DateTimeOffset AcquiredAt { get; }

    public PackageLockConflictException(
        string packagePath,
        string ownerJobId,
        string ownerAgentInstanceId,
        DateTimeOffset acquiredAt)
        : base($"Package at '{packagePath}' is locked by agent {ownerAgentInstanceId} (job {ownerJobId}) since {acquiredAt:O}.")
    {
        PackagePath = packagePath;
        OwnerJobId = ownerJobId;
        OwnerAgentInstanceId = ownerAgentInstanceId;
        AcquiredAt = acquiredAt;
    }
}
