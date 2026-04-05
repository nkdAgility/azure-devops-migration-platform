using System.Collections.Generic;
using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Abstraction over the date-window WIQL counting strategy.
/// Enables mocking in tests without requiring a real Azure DevOps connection.
/// </summary>
public interface IWorkItemQueryWindowStrategy
{
    /// <summary>
    /// Enumerates successive date windows for the given project,
    /// yielding work item IDs for each window in reverse-chronological order.
    /// </summary>
    IAsyncEnumerable<WorkItemQueryWindow> EnumerateWindowsAsync(
        string orgOrCollection,
        string project,
        string pat,
        WorkItemQueryWindowOptions? options = null,
        CancellationToken cancellationToken = default);
}
