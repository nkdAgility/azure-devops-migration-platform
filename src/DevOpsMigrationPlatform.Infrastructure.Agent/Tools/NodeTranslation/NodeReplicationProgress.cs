// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Checkpoint record persisted as package content at
/// <c>Nodes/replication-progress.json</c> via <c>NodesOrchestrator.SaveProgressAsync</c>.
/// Tracks which classification nodes have been confirmed present in the target
/// during a replication pass.
/// </summary>
public sealed class NodeReplicationProgress
{
    /// <summary>
    /// Case-insensitive set of node paths confirmed present in the target.
    /// </summary>
    public HashSet<string> ReplicatedPaths { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
