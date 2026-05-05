// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Orchestrates a full inventory run across all configured organisations.
/// Reads its own <see cref="Options.MigrationPlatformOptions"/> via DI —
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

    /// <summary>
    /// Runs inventory against a single pre-resolved endpoint — the same mechanism other
    /// modules use when connecting via <see cref="ISourceEndpointInfo"/>. No factory or
    /// <see cref="Options.MigrationPlatformOptions"/> required.
    /// </summary>
    /// <param name="endpoint">Fully resolved endpoint with auth (e.g. from <c>ISourceEndpointInfo.ToOrganisationEndpoint()</c>).</param>
    /// <param name="projects">Optional explicit project list. Empty or null = discover all projects.</param>
    /// <param name="completedProjectKeys">Projects to skip (resume support).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        OrganisationEndpoint endpoint,
        IReadOnlyList<string>? projects = null,
        HashSet<string>? completedProjectKeys = null,
        CancellationToken cancellationToken = default);
}
