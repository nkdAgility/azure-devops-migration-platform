// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Abstracts the single WIQL query operation needed by
/// <see cref="DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export.WorkItemQueryWindowStrategy"/>
/// so that the strategy can be unit-tested without a live Azure DevOps connection.
/// </summary>
public interface IWiqlQueryClient
{
    /// <summary>
    /// Executes a WIQL query and returns a domain result containing the matching work item IDs.
    /// </summary>
    Task<WorkItemQueryResult> QueryByWiqlAsync(
        string wiql,
        string project,
        CancellationToken cancellationToken = default);
}
