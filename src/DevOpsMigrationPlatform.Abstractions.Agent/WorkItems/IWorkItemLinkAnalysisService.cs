// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

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
    /// <param name="savedContinuationToken">Optional continuation token from a prior run to resume from.
    /// When provided, enumeration resumes from the saved position instead of starting from the beginning.</param>
    /// <param name="continuationCheckpointWriter">Optional callback invoked per-batch with the latest
    /// <see cref="BatchContinuationToken"/>. Callers use this to persist resume state at checkpoint intervals.</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation.</param>
    /// <returns>An async enumerable of DependencyProgressEvent records.</returns>
    IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
        MigrationEndpointOptions endpoint,
        string project,
        string? wiqlFilter = null,
        BatchContinuationToken? savedContinuationToken = null,
        Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
        CancellationToken cancellationToken = default);
}
