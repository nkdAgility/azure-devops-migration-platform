// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Enumerates the full classification tree from the source project.
/// Export-only — never called at import time.
/// </summary>
public interface IClassificationTreeReader
{
    /// <summary>Enumerates all area node paths from the source project.</summary>
    IAsyncEnumerable<string> EnumerateAreaNodesAsync(CancellationToken ct);

    /// <summary>Enumerates all iteration nodes (with dates) from the source project.</summary>
    IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(CancellationToken ct);

    /// <summary>
    /// Returns the total count of area and iteration classification nodes for
    /// <paramref name="project"/> without writing any files to the artefact store.
    /// Used during inventory to obtain per-project node counts across multiple projects.
    /// Returns <c>0</c> and logs a warning if the underlying API call fails.
    /// </summary>
    Task<int> CountNodesAsync(string project, CancellationToken ct);
}
