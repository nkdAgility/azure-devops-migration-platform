using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Orchestrates a full inventory run across all configured organisations.
/// Reads its own <see cref="Options.DiscoveryOptions"/> via DI —
/// callers just stream the results.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Streams <see cref="InventoryProgressEvent"/> records across every enabled organisation
    /// and project. Each event reflects running totals after completing a date window.
    /// The final event per project has <see cref="InventoryProgressEvent.IsComplete"/> = <c>true</c>.
    /// </summary>
    /// <param name="completedProjectKeys">
    /// Optional set of project keys (<c>"{url}|{projectName}"</c>) that are already complete
    /// from a previous run. Projects whose key is in this set are skipped entirely — no API
    /// calls are made for them. Pass <c>null</c> or an empty set for a fresh run.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        HashSet<string>? completedProjectKeys = null,
        CancellationToken cancellationToken = default);
}
