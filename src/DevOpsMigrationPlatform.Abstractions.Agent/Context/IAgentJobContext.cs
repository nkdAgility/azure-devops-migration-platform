// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Read-only view of the current agent job's execution context.
/// Scoped to a single agent job — constructed once when the job starts, never mutated.
/// </summary>
public interface IAgentJobContext
{
    /// <summary>
    /// The execution mode for this job: "Export", "Import", "Prepare", or "Migrate".
    /// </summary>
    string Mode { get; }

    /// <summary>
    /// Resolved, expanded absolute path to the migration package directory on disk.
    /// Never contains '~' or environment variable expansions.
    /// </summary>
    string PackagePath { get; }

    /// <summary>
    /// The declared config schema version from migration-config.json (e.g. "2.0").
    /// </summary>
    string ConfigVersion { get; }
}
