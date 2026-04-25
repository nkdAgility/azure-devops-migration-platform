using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Full per-org, per-project state for late-joining clients.
/// Pushed by the agent every 5 minutes or at project boundaries.
/// The Control Plane stores only the latest <c>JobSnapshot</c> per job — overwrite, no history.
/// </summary>
public record JobSnapshot
{
    /// <summary>UTC timestamp when this snapshot was produced.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Organisation snapshots with nested project detail.</summary>
    public IReadOnlyList<OrgSnapshot> Organisations { get; init; } = [];
}
