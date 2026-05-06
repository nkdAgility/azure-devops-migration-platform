// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;
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
        var options = BuildMigrationPlatformOptions(organisations, policies);

        // Build a generator config that merges all org-level generators so the
        // SimulatedWorkItemDiscoveryService can look up any project by name.
        // (The DI singleton has an empty config because the generator is loaded
        // from the job config at runtime, not at DI startup.)
        var mergedGenerator = BuildMergedGenerator(organisations);
        var workItemDiscovery = mergedGenerator.Projects?.Count > 0
            ? new SimulatedWorkItemDiscoveryService(mergedGenerator)
            : _workItemDiscovery;

        return new InventoryService(
            new OptionsWrapper<MigrationPlatformOptions>(options),
            workItemDiscovery,
            _projectDiscovery,
            _repoDiscovery);
    }

    private static SimulatedGeneratorConfig BuildMergedGenerator(IReadOnlyList<ScopedOrganisationEndpoint> organisations)
    {
        var allProjects = new List<SimulatedProjectConfig>();
        foreach (var org in organisations)
        {
            var gen = (org.Endpoint as SimulatedEndpointOptions)?.Generator;
            if (gen?.Projects is { Count: > 0 } projects)
                allProjects.AddRange(projects);
        }
        return new SimulatedGeneratorConfig { Projects = allProjects };
    }

    private static MigrationPlatformOptions BuildMigrationPlatformOptions(
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

        return new MigrationPlatformOptions
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
