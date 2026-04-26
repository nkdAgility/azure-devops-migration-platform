using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Enumerates the full classification tree from the source project.
/// Export-only — never called at import time.
/// </summary>
public interface IClassificationTreeReader
{
    /// <summary>Enumerates all area node paths from the source project identified by <paramref name="endpoint"/>.</summary>
    IAsyncEnumerable<string> EnumerateAreaNodesAsync(MigrationEndpointOptions endpoint, CancellationToken ct);

    /// <summary>Enumerates all iteration nodes (with dates) from the source project identified by <paramref name="endpoint"/>.</summary>
    IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(MigrationEndpointOptions endpoint, CancellationToken ct);
}
