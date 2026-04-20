# Contract: IPackageLockService (new)

**Namespace**: `DevOpsMigrationPlatform.Abstractions`  
**Project**: `DevOpsMigrationPlatform.Abstractions`  
**Implementation**: `PackageLockFileService` (Infrastructure)  
**Registration**: `services.AddPackageLockServices()` extension method

## Interface

```csharp
/// <summary>
/// Acquires an exclusive lock on a migration package directory.
/// Guards against two agents running concurrently against the same package.
/// </summary>
public interface IPackageLockService
{
    /// <summary>
    /// Acquires an exclusive lock on <paramref name="packagePath"/>.
    /// Returns an <see cref="IAsyncDisposable"/> that releases the lock on dispose.
    /// </summary>
    /// <exception cref="PackageLockConflictException">
    /// Thrown when a live lock already exists (owning process is still running).
    /// The second agent's job must be failed/bounced by the caller.
    /// </exception>
    Task<IAsyncDisposable> AcquireAsync(string packagePath, string jobId, CancellationToken ct);
}
```

## Lock file location

`{packagePath}/Checkpoints/agent.lock`

## Acquire algorithm

```
lockFilePath = Checkpoints/agent.lock
try:
    open file with FileMode.CreateNew (atomic — fails if exists)
    write JSON: { jobId, pid: Environment.ProcessId, acquiredAt: UtcNow }
    return handle that deletes file on DisposeAsync
catch FileNotFoundException:
    read existing lock file
    if Process.GetProcessById(pid) throws ArgumentException (process not found):
        // stale lock — delete and retry once
        File.Delete(lockFilePath)
        goto try
    else:
        throw PackageLockConflictException(ownerJobId, ownerPid, acquiredAt)
```

## `PackageLockConflictException`

```csharp
public sealed class PackageLockConflictException : Exception
{
    public string PackagePath { get; init; }
    public string OwnerJobId { get; init; }
    public int OwnerPid { get; init; }
    public DateTimeOffset AcquiredAt { get; init; }
}
```

## Caller contract (`JobAgentWorker.ExecuteMigrationAsync`)

```csharp
await using var packageLock = await _packageLockService.AcquireAsync(
    packagePath, job.JobId, ct);
// ... module execution ...
// lock released on dispose
```

`PackageLockConflictException` propagates up and is caught by `PollAndExecuteAsync`, which signals the job as failed (hard bounce).

## Scope note

Defined in `Abstractions` as a job-engine–level cross-cutting concern. Not module-specific. Export jobs and future module types may acquire it when they gain write access to the package.

## Topology note

For `file://` packages: atomic `FileMode.CreateNew` is the mechanism.  
For cloud topologies (Azure Blob): the control plane's lease system prevents duplicate agent assignment. The lock file adds defence-in-depth but the PID liveness check is not applicable cross-host. Future: cloud implementations may use blob conditional writes or ETag-based lease. This is deferred — cloud conflict prevention is already handled by the control plane.
