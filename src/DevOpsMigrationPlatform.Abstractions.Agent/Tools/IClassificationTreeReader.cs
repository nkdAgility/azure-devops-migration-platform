// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;

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
}
