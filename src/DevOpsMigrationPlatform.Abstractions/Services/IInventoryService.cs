using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Counts work items and revisions per project using date-windowed queries.
/// Implementations must not buffer all work item IDs into memory.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Streams <see cref="InventoryProgressEvent"/> records for <paramref name="project"/>.
    /// Each event reflects the latest running totals after completing a date window.
    /// The final event has <see cref="InventoryProgressEvent.IsComplete"/> set to <c>true</c>.
    /// </summary>
    IAsyncEnumerable<InventoryProgressEvent> CountWorkItemsAsync(
        string orgOrCollection,
        string project,
        string pat,
        CancellationToken cancellationToken = default);
}
