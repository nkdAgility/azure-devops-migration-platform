using System;
using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Service for analysing work item links within a single organisation or source.
/// Implementations exist for Azure DevOps Services, Team Foundation Server, and simulated sources.
/// </summary>
public interface IWorkItemLinkAnalysisService
{
    /// <summary>
    /// Analyses all work item links in a specific organisation and project.
    /// Streams results as DependencyProgressEvent records.
    /// </summary>
    /// <param name="endpoint">The resolved connection context for the organisation or collection.</param>
    /// <param name="project">The name of the project within the organisation.</param>
    /// <param name="wiqlFilter">Optional WIQL expression to filter the work items.
    /// If null or empty, all work items are included.</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation.</param>
    /// <returns>An async enumerable of DependencyProgressEvent records.</returns>
    IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
        OrganisationEndpoint endpoint,
        string project,
        string? wiqlFilter = null,
        CancellationToken cancellationToken = default);
}
