// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Factories;

/// <summary>
/// Creates <see cref="IDependencyDiscoveryService"/> instances backed entirely by
/// <see cref="SimulatedWorkItemLinkAnalysisService"/> — no network calls are made.
/// Registered by <see cref="SimulatedServiceCollectionExtensions.AddSimulatedDependencyAnalysis"/>
/// via <c>TryAddSingleton</c> so that it only takes effect when no ADO factory is registered.
/// </summary>
public sealed class SimulatedDependencyDiscoveryServiceFactory : IDependencyDiscoveryServiceFactory
{
    private readonly IWorkItemLinkAnalysisService _linkAnalysisService;

    public SimulatedDependencyDiscoveryServiceFactory(
        [FromKeyedServices("Simulated")] IWorkItemLinkAnalysisService linkAnalysisService)
    {
        _linkAnalysisService = linkAnalysisService ?? throw new ArgumentNullException(nameof(linkAnalysisService));
    }

    /// <inheritdoc/>
    public IDependencyDiscoveryService Create(
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        JobPolicies policies)
        => new SimulatedDependencyDiscoveryService(_linkAnalysisService, organisations);

    /// <inheritdoc/>
    public IDependencyDiscoveryService CreateForProject(
        IReadOnlyList<ScopedOrganisationEndpoint> allOrganisations,
        string orgUrl,
        string projectName,
        JobPolicies policies)
    {
        // Scope to the single project.
        var scoped = new List<ScopedOrganisationEndpoint>();
        foreach (var org in allOrganisations)
        {
            if (string.Equals(org.Endpoint.GetResolvedUrl(), orgUrl, StringComparison.OrdinalIgnoreCase))
            {
                scoped.Add(new ScopedOrganisationEndpoint
                {
                    Endpoint = org.Endpoint,
                    Projects = new List<string> { projectName }
                });
                break;
            }
        }

        if (scoped.Count == 0 && allOrganisations.Count > 0)
        {
            // No matching org found — use first org with the single project.
            scoped.Add(new ScopedOrganisationEndpoint
            {
                Endpoint = allOrganisations[0].Endpoint,
                Projects = new List<string> { projectName }
            });
        }

        return new SimulatedDependencyDiscoveryService(_linkAnalysisService, scoped);
    }

    /// <summary>
    /// Lightweight <see cref="IDependencyDiscoveryService"/> that delegates to
    /// <see cref="IWorkItemLinkAnalysisService"/> for each configured project.
    /// For the Simulated connector, the link analysis service returns no links.
    /// </summary>
    private sealed class SimulatedDependencyDiscoveryService : IDependencyDiscoveryService
    {
        private readonly IWorkItemLinkAnalysisService _linkAnalysis;
        private readonly IReadOnlyList<ScopedOrganisationEndpoint> _organisations;

        internal SimulatedDependencyDiscoveryService(
            IWorkItemLinkAnalysisService linkAnalysis,
            IReadOnlyList<ScopedOrganisationEndpoint> organisations)
        {
            _linkAnalysis = linkAnalysis;
            _organisations = organisations;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
            HashSet<string>? completedProjectKeys = null,
            string? wiqlFilter = null,
            string? inProgressProjectKey = null,
            BatchContinuationToken? inProgressToken = null,
            Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var org in _organisations)
            {
                var endpoint = org.Endpoint;
                foreach (var project in org.Projects)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await foreach (var evt in _linkAnalysis.AnalyseLinksAsync(
                        endpoint,
                        project,
                        wiqlFilter,
                        savedContinuationToken: null,
                        continuationCheckpointWriter: null,
                        cancellationToken: cancellationToken).ConfigureAwait(false))
                    {
                        yield return evt;
                    }
                }
            }
        }
    }
}
