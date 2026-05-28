// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Pluggable connector-facing strategy for target lookup/provenance semantics.
/// Cache/id-map lifecycle remains in shared WorkItem resolution processing flow.
/// See <c>specs/013-ado-workitems-import/contracts/IWorkItemTarget.md</c> for details.
/// </summary>
public interface IWorkItemResolutionStrategy
{
    /// <summary>
    /// Seed <paramref name="idMapStore"/> from connector-side lookup behavior at import startup.
    /// Called by shared WorkItem resolution processing before enumeration begins.
    /// A no-op implementation is acceptable (e.g. <c>NullResolutionStrategy</c>).
    /// </summary>
    Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct);

    /// <summary>
    /// Attempt to resolve a single source work item ID against the target connector
    /// as a live fallback when the ID is not found in the local map.
    /// Returns the target ID if found, <see langword="null"/> otherwise.
    /// May return <see langword="null"/> immediately for strategies that do not support live lookup.
    /// </summary>
    Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct);

    /// <summary>
    /// After creating a new work item in the target, write the provenance marker
    /// (e.g. a custom field value or hyperlink) so that the mapping is discoverable
    /// in future import runs.
    /// A no-op implementation is acceptable when no provenance tracking is configured.
    /// </summary>
    Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct);
}
