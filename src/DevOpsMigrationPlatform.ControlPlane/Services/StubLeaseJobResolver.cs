using System;
using System.Collections.Concurrent;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// Phase-1 stub implementation of <see cref="ILeaseJobResolver"/>.
/// Stores lease ↔ job mappings in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// that lives only for the lifetime of this singleton.
/// Replace with a durable store once the full lease protocol is implemented.
/// </summary>
public sealed class StubLeaseJobResolver : ILeaseJobResolver
{
    private readonly ConcurrentDictionary<string, Guid> _map = new(StringComparer.Ordinal);

    public Guid? ResolveJobId(string leaseId) =>
        _map.TryGetValue(leaseId, out var jobId) ? jobId : null;

    public void RegisterLease(string leaseId, Guid jobId) =>
        _map[leaseId] = jobId;

    public void UnregisterLease(string leaseId) =>
        _map.TryRemove(leaseId, out _);
}
