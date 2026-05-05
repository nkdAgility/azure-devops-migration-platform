// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Factories;

/// <summary>
/// Creates a configured <see cref="IInventoryService"/> backed entirely by Simulated discovery
/// services — no network calls. Used when the agent source connector is <c>Simulated</c>
/// and no explicit organisations list is present in the job config.
/// </summary>
internal sealed class SimulatedInventoryServiceFactory : IInventoryServiceFactory
{
    private readonly IWorkItemDiscoveryService _workItemDiscovery;
    private readonly IProjectDiscoveryService _projectDiscovery;
    private readonly IRepoDiscoveryService _repoDiscovery;

    public SimulatedInventoryServiceFactory(
        [FromKeyedServices("Simulated")] IWorkItemDiscoveryService workItemDiscovery,
        [FromKeyedServices("Simulated")] IProjectDiscoveryService projectDiscovery,
        [FromKeyedServices("Simulated")] IRepoDiscoveryService repoDiscovery)
    {
        _workItemDiscovery = workItemDiscovery ?? throw new ArgumentNullException(nameof(workItemDiscovery));
        _projectDiscovery = projectDiscovery ?? throw new ArgumentNullException(nameof(projectDiscovery));
        _repoDiscovery = repoDiscovery ?? throw new ArgumentNullException(nameof(repoDiscovery));
    }

    /// <inheritdoc/>
    public IInventoryService Create(
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        JobPolicies policies)
    {
        var options = BuildMigrationOptions(organisations, policies);
        return new InventoryService(
            new OptionsWrapper<MigrationOptions>(options),
            _workItemDiscovery,
            _projectDiscovery,
            _repoDiscovery);
    }

    private static MigrationOptions BuildMigrationOptions(
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        JobPolicies policies)
    {
        var entries = organisations.Count > 0
            ? organisations.Select(o =>
            {
                var sim = o.Endpoint as SimulatedEndpointOptions;
                return (OrganisationEntry)new SimulatedOrganisationEntry
                {
                    Type = "Simulated",
                    Projects = new List<string>(o.Projects),
                    Enabled = true,
                    Generator = sim?.Generator ?? new SimulatedGeneratorConfig()
                };
            }).ToList()
            : new List<OrganisationEntry>
            {
                new SimulatedOrganisationEntry { Type = "Simulated", Enabled = true }
            };

        return new MigrationOptions
        {
            Package = new MigrationPackageOptions { WorkingDirectory = "(managed-by-agent)" },
            Policies = new MigrationPoliciesOptions
            {
                Retries = new MigrationRetriesOptions { Max = policies.MaxRetries },
                Throttle = new MigrationThrottleOptions { MaxConcurrency = policies.MaxConcurrency },
                Checkpoints = new MigrationCheckpointsOptions { Interval = policies.CheckpointIntervalSeconds }
            },
            Organisations = entries
        };
    }
}
