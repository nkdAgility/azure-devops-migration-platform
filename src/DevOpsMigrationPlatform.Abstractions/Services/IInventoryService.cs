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
    IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        CancellationToken cancellationToken = default);
}
