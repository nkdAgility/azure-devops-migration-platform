using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Abstraction over the date-window WIQL chunking strategy.
/// Keeps each query under the WIQL hard limit by walking successive
/// date windows. Platform-agnostic — implementations exist for both
/// Azure DevOps REST API and TFS Object Model.
/// </summary>
public interface IWorkItemQueryWindowStrategy
{
    /// <summary>
    /// Enumerates successive date windows for the given project,
    /// yielding work item IDs for each window in reverse-chronological order.
    /// </summary>
    IAsyncEnumerable<WorkItemQueryWindow> EnumerateWindowsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemQueryWindowOptions? options = null,
        CancellationToken cancellationToken = default);
}
