using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Streams work items with field projection and in-process filter evaluation.
/// Sits above <see cref="IWorkItemQueryWindowStrategy"/> and below callers
/// (Inventory, Dependency, Catalog).
/// </summary>
public interface IWorkItemFetchService
{
    /// <summary>
    /// Fetches work items from the source system with only the declared fields,
    /// applying in-process filters before yielding each item.
    /// </summary>
    /// <param name="endpoint">Resolved connection context.</param>
    /// <param name="project">Target project name.</param>
    /// <param name="scope">Query scope: required fields, optional filters, optional base WIQL WHERE clause.</param>
    /// <param name="cancellationToken">Cancellation token — must be propagated to all internal async operations.</param>
    /// <returns>
    /// An asynchronous stream of fetched work items. Each item contains only the requested fields.
    /// Items that do not satisfy <see cref="WorkItemFetchScope.FilterOptions"/> are excluded.
    /// Empty when the underlying query returns zero IDs.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="scope"/>.<see cref="WorkItemFetchScope.Fields"/> is null or empty.
    /// </exception>
    IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default);
}
