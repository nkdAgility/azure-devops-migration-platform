// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

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
    /// <exception cref="ResumeRejectedException">
    /// Thrown when <see cref="WorkItemFetchScope.ResumeEnabled"/> is true and the query
    /// fingerprint does not match the saved token. Safety net for callers that skip the pre-check.
    /// </exception>
    IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the resume decision without starting enumeration. Callers can
    /// inspect the result and choose recovery before calling <see cref="FetchAsync"/>.
    /// </summary>
    /// <param name="endpoint">Resolved connection context.</param>
    /// <param name="project">Target project name.</param>
    /// <param name="scope">Query scope (must include resume fields).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resume decision that <see cref="FetchAsync"/> would apply.</returns>
    Task<ResumeDecision> EvaluateResumeDecisionAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default);
}
