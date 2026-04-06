using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Provides inventory data from a Team Foundation Server collection
/// by spawning a .NET Framework subprocess. Implementations live in
/// <c>CLI.Migration</c> per Guardrail #19.
/// </summary>
public interface ITfsInventoryProvider
{
    /// <summary>
    /// Streams <see cref="InventoryProgressEvent"/> records for a TFS collection.
    /// When <paramref name="project"/> is null or empty and <paramref name="allProjects"/>
    /// is <c>true</c>, all projects in the collection are inventoried.
    /// </summary>
    IAsyncEnumerable<InventoryProgressEvent> RunAsync(
        string collectionUrl,
        string? project,
        string pat,
        bool allProjects,
        CancellationToken cancellationToken = default);
}
