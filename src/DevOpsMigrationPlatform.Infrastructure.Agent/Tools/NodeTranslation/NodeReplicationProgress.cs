// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Checkpoint record persisted in <c>IStateStore</c> under key
/// <c>nodestructure-nodes-confirmed</c>. Tracks which classification nodes have been
/// confirmed present in the target during a replication pass.
/// </summary>
public sealed class NodeReplicationProgress
{
    /// <summary>
    /// State store key for this checkpoint.
    /// </summary>
    public const string StateKey = "nodestructure-nodes-confirmed";

    /// <summary>
    /// Case-insensitive set of node paths confirmed present in the target.
    /// </summary>
    public HashSet<string> ReplicatedPaths { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
