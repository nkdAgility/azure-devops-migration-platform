// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Per-project capture module for the Dependencies job.
/// Each <c>capture.dependencies.{org}.{project}</c> plan task targets exactly one project.
/// The module creates a single-project scope, invokes <see cref="IDependencyOrchestrator"/>
/// for that project only, and relies on the orchestrator's cursor-based resume to skip
/// previously captured projects.
/// </summary>
internal sealed class DependenciesModule : ModuleBase
{
    private readonly IDependencyDiscoveryServiceFactory _discoveryFactory;
    private readonly IDependencyOrchestrator _orchestrator;

    public DependenciesModule(
        IDependencyDiscoveryServiceFactory discoveryFactory,
        IDependencyOrchestrator orchestrator,
        ILogger<DependenciesModule> logger) : base(logger)
    {
        _discoveryFactory = discoveryFactory;
        _orchestrator = orchestrator;
    }

    public override string Name => "Dependencies";
    public override bool SupportsInventory => true;

    public override async Task InventoryAsync(InventoryContext ctx, CancellationToken ct)
    {
        var orgUrl = ctx.SourceEndpoint.ResolvedUrl;

        // Find the matching ScopedOrganisationEndpoint so we carry the original auth/endpoint options.
        var matchingOrg = ctx.Organisations.FirstOrDefault(o =>
            string.Equals(o.Endpoint.GetResolvedUrl(), orgUrl, StringComparison.OrdinalIgnoreCase));

        if (matchingOrg is null)
            throw new InvalidOperationException(
                $"DependenciesModule: no ScopedOrganisationEndpoint found for org URL '{orgUrl}'. " +
                "Ensure baseInventoryContext.Organisations is populated before executing capture tasks.");

        // Restrict to the single project targeted by this plan task.
        var singleProjectOrg = new ScopedOrganisationEndpoint
        {
            Endpoint = matchingOrg.Endpoint,
            Projects = new List<string> { ctx.Project },
            Scopes = matchingOrg.Scopes
        };

        var policies = ctx.Policies;
        var service = _discoveryFactory.Create(new[] { singleProjectOrg }, policies);

        await _orchestrator.AnalyseAsync(
            service,
            new OrganisationsAnalyseContext
            {
                Job = ctx.Job,
                ArtefactStore = ctx.ArtefactStore,
                StateStore = ctx.StateStore,
                ProgressSink = ctx.ProgressSink,
                Policies = policies,
                Organisations = new[] { singleProjectOrg }
            },
            policies,
            policies.CheckpointIntervalSeconds,
            ct).ConfigureAwait(false);
    }
}
