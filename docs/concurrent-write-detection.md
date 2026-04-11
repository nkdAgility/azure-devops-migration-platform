# Concurrent Write Detection & Lease Protocol

## Overview

The Azure DevOps Migration Platform protects migration packages from concurrent writes through a **lease-based protocol**. A migration package is bound to a single agent via a time-limited lease; only the lease holder may write to the package. This prevents data corruption from concurrent writes.

## Problem Statement

If two agents attempt to write to the same package simultaneously without coordination:

```
Agent A                          Agent B
└─ WriteAsync("path/file")       └─ WriteAsync("path/file")
   └─ Read current file            └─ Read current file  
   └─ Modify                      └─ Modify
   └─ Write back                  └─ Write back
                                   [Last-write-wins: Agent B's data overwrites Agent A's]
```

This leads to **silent data loss**. Example:
- Package is at revision 100
- Agent A processes revisions 101–150 and writes `Checkpoints/revision.cursor`
- Agent B processes revisions 101–120 (slower, late assignment) and overwrites the same cursor
- Result: Revisions 121–150 are never re-attempted if Agent A crashes; cursor points to wrong location

## Solution: Lease-Based Write Protection

### Protocol

1. **Lease Acquisition**: Before migration starts, an agent requests a lease from the Control Plane
   ```
   Agent → Control Plane: "Assign me job X for package P"
   ← Control Plane: "Leased until {timestamp}; LeaseID = {uuid}"
   ```

2. **Lease Binding**: The agent binds all writes to the lease
   ```
   Agent → Package (via IArtefactStore): WriteAsync(path, content, leaseToken)
   ```

3. **Write Authorization**: The package store verifies the lease before accepting writes
   ```csharp
   // Pseudocode (implementation-dependent)
   if (!IsValidLease(leaseToken))
       throw LeaseExpiredException("Not authorized to write");
   WriteFile(path, content);
   ```

4. **Lease Renewal**: Agent periodically renews the lease to indicate it is still alive
   ```
   Agent → Control Plane: "Renew lease {uuid}, I'm still running"
   ← Control Plane: "Lease renewed until {new_timestamp}"
   ```

5. **Lease Expiration**: If the agent crashes or stalls, its lease expires after {TTL}
   ```
   Control Plane watches lease expiry
   └─ After TTL, lease is revoked
   └─ Package is available for re-assignment to another agent
   └─ Ex-holder's pending writes will fail (implementation-dependent)
   ```

## Implementation

### At the `IArtefactStore` Level

The interface **does not directly enforce the lease check** (that is caller's responsibility); instead, it documents the protocol:

```csharp
public interface IArtefactStore
{
    /// <summary>
    /// Writes content to the specified path within the package.
    /// 
    /// LEASE REQUIREMENT: Only the lease holder may call this method.
    /// Callers must establish a valid lease before writing. Implementations
    /// may verify the lease or rely on the calling layer to enforce it.
    /// </summary>
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);
}
```

### At the Agent Level

The Migration Agent holds the lease for the duration of the job:

```csharp
public class MigrationAgent
{
    public async Task RunJobAsync(MigrationJob job, string leaseToken, CancellationToken ct)
    {
        // Verify lease is still valid before each critical operation
        await _leaseService.RenewAsync(leaseToken, ct);
        
        // Pass job to Job Engine; all writes go through artefactStore
        // which validates the lease (or relies on protocol contract)
        var result = await _jobEngine.ExecuteAsync(job, artefactStore, ct);
    }
}
```

### At the Control Plane Level

The Control Plane manages lease lifecycle:

```csharp
public class LeaseService
{
    public async Task<LeaseToken> AcquireAsync(
        string packageId, string agentId, TimeSpan ttl, CancellationToken ct)
    {
        // Create lease entry
        var token = Guid.NewGuid().ToString();
        var leases = _db.Leases;
        leases.Add(new()
        {
            Token = token,
            PackageId = packageId,
            AgentId = agentId,
            ExpiresAt = Clock.Now + ttl
        });
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> ValidateAsync(string leaseToken, CancellationToken ct)
    {
        var lease = await _db.Leases.FirstOrDefaultAsync(l => l.Token == leaseToken, ct);
        return lease != null && lease.ExpiresAt > Clock.Now;
    }
}
```

## Design Guarantees

### ✅ Serialization

Only one agent holds a valid lease on a package at any given time. Writes are serialized by lease holder.

### ✅ Atomicity

A single `WriteAsync(path, content)` call is atomic:
- Either the file is written in full, or not at all
- No partial contents visible to readers
- Implementation uses temp-file-and-rename or database transactions as needed

### ✅ Failure Recovery

If the lease holder crashes:
1. Lease expires (TTL seconds later)
2. Control Plane revokes the lease
3. Package becomes available for re-assignment
4. Next agent resumes from cursor (idempotency via checkpoints)

If the package store detects an expired lease during a write, it fails the write:
```
Agent A (stale lease) → WriteAsync(...)
  ← LeaseExpiredException("Lease expired")
  → Agent A detects and exits gracefully
Control Plane detects exit, re-assigns to Agent B
Agent B resumes from last checkpoint
```

## Testing & Validation

### Unit Tests

`IArtefactStore` implementations must test:
- ✅ Write atomicity (file is either complete or unchanged)
- ✅ Concurrent-read correctness (readers see consistent state)

### Integration Tests

The Job Engine tests must verify:
- ✅ Cursor is updated last (transactional boundary)
- ✅ If export fails after writing N revisions, cursor is at N–1 (resumable state)
- ✅ Multiple agents can safely run on different packages in parallel

### Lease Renewal Tests

The Migration Agent tests must verify:
- ✅ Lease is renewed every {interval} seconds
- ✅ If renewal fails, agent exits with error
- ✅ If lease expires during migration, writes fail fast

## Current Status

**As of 2026-04:**
- ✅ Lease protocol is implemented in `ControlPlaneHost` and `MigrationAgent`
- ✅ `IArtefactStore` interface documents the protocol requirement
- ✅ `FileSystemArtefactStore` (local dev) relies on process-level file locks (implicit serialization)
- ⚠️ `AzureBlobArtefactStore` (cloud) uses Azure leases for blob-level concurrency control
- ⚠️ **Documentation completeness**: Interface docs now explain the protocol explicitly; this document formalizes the design

## References

- [Architecture: Migration Agent](./migration-agent.md#lease-protocol)
- [Storage Abstraction](../src/DevOpsMigrationPlatform.Abstractions/Storage/IArtefactStore.cs)
- [Control Plane: Lease Service](../docs/control-plane.md#lease-management)
- [Data Integrity Best Practices](./validation.md#tier-1---pre-flight-validation)
