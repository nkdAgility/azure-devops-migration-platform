// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Strongly-typed representation of the per-project inventory file written to
/// <c>{orgSlug}/{project}/inventory.json</c>. All module inventory counts are
/// merged into this single file; no per-module inventory files are written.
/// This is the canonical inventory file-format contract (ADR-0023 / VS-H2).
/// </summary>
public sealed record ProjectInventoryData
{
    public DateTimeOffset GeneratedAt { get; init; }
    public string OrgUrl { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public long WorkItems { get; init; }
    public long Revisions { get; init; }
    public int Repos { get; init; }
    public int Identities { get; init; }
    public int Nodes { get; init; }
    public int Teams { get; init; }
    public bool IsComplete { get; init; }
    public string? Error { get; init; }
    /// <summary>Work item count by System.AreaPath. Null when not collected.</summary>
    public Dictionary<string, int>? AreaPathCounts { get; init; }
}

/// <summary>
/// Canonical port for reading the per-project inventory file (ADR-0023 / VS-H2).
/// </summary>
public interface IProjectInventoryReader
{
    /// <summary>
    /// Reads the existing per-project inventory file, or returns an empty record when the
    /// file is missing or unreadable.
    /// </summary>
    Task<ProjectInventoryData> ReadAsync(
        IPackageAccess package,
        string orgSlug,
        string projectName,
        CancellationToken ct);
}

/// <summary>
/// Canonical port for merging module inventory counts into the per-project inventory file
/// (ADR-0023 / VS-H2). Modules update only their own count field without clobbering data
/// written by another module.
/// </summary>
public interface IProjectInventoryWriter
{
    /// <summary>
    /// Reads the existing per-project inventory file (or an empty record),
    /// applies the supplied delta fields, and writes the result back.
    /// </summary>
    Task MergeAsync(
        IPackageAccess package,
        string orgSlug,
        string projectName,
        string? orgUrl = null,
        long? workItems = null,
        long? revisions = null,
        int? repos = null,
        int? identities = null,
        int? nodes = null,
        int? teams = null,
        bool? isComplete = null,
        string? error = null,
        Dictionary<string, int>? areaPaths = null,
        CancellationToken ct = default);
}
