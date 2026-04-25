using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Streams work item revisions from a source system.
/// Implementations must yield one revision at a time and MUST NOT buffer all results into memory.
/// The export orchestrator calls this; no module calls source APIs directly.
/// </summary>
public interface IWorkItemRevisionSource
{
    /// <summary>
    /// Returns all work item revisions from the source, in ascending revision order per work item.
    /// The stream must be lazy — revisions are fetched on demand as the consumer iterates.
    /// </summary>
    IAsyncEnumerable<WorkItemRevision> GetRevisionsAsync(CancellationToken cancellationToken);
}
