// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Phase-1 stub implementation of <see cref="ILeaseJobResolver"/>.
/// Stores lease ↔ job mappings in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// that lives only for the lifetime of this singleton.
/// Replace with a durable store once the full lease protocol is implemented.
/// </summary>
public sealed class StubLeaseJobResolver : ILeaseJobResolver
{
    private sealed record LeaseEntry(Guid JobId, DateTimeOffset? LastHeartbeat = null);

    private readonly ConcurrentDictionary<string, LeaseEntry> _map = new(StringComparer.Ordinal);

    public Guid? ResolveJobId(string leaseId) =>
        _map.TryGetValue(leaseId, out var entry) ? entry.JobId : null;

    public void RegisterLease(string leaseId, Guid jobId) =>
        _map[leaseId] = new LeaseEntry(jobId);

    public void UnregisterLease(string leaseId) =>
        _map.TryRemove(leaseId, out _);

    public bool RecordHeartbeat(string leaseId)
    {
        if (!_map.TryGetValue(leaseId, out var entry))
            return false;
        _map[leaseId] = entry with { LastHeartbeat = DateTimeOffset.UtcNow };
        return true;
    }

    public DateTimeOffset? GetLastHeartbeat(string leaseId) =>
        _map.TryGetValue(leaseId, out var entry) ? entry.LastHeartbeat : null;
}
