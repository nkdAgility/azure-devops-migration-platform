// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Resolves a lease identifier to the corresponding job identifier.
/// The lease-to-job mapping is established when the Migration Agent
/// calls GET /agents/lease and is stored by the implementation.
/// </summary>
public interface ILeaseJobResolver
{
    /// <summary>
    /// Returns the job id for <paramref name="leaseId"/>, or <c>null</c>
    /// if the lease is not recognised.
    /// </summary>
    Guid? ResolveJobId(string leaseId);

    /// <summary>
    /// Records a lease ↔ job mapping when a lease is granted to an agent.
    /// </summary>
    void RegisterLease(string leaseId, Guid jobId);

    /// <summary>
    /// Removes the mapping when the lease is released or expires.
    /// </summary>
    void UnregisterLease(string leaseId);
}
