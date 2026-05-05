// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories;

/// <summary>
/// Creates a configured <see cref="IInventoryService"/> from <see cref="ScopedOrganisationEndpoint"/>
/// entries carried in the migration config. Used by <c>InventoryDiscoveryModule</c>
/// so the agent does not need a config file at runtime.
/// </summary>
internal sealed class InventoryServiceFactory : IInventoryServiceFactory
{
    private readonly IWorkItemDiscoveryService _workItemDiscovery;
    private readonly IProjectDiscoveryService _projectDiscovery;
    private readonly IRepoDiscoveryService _repoDiscovery;

    public InventoryServiceFactory(
        IWorkItemDiscoveryService workItemDiscovery,
        IProjectDiscoveryService projectDiscovery,
        IRepoDiscoveryService repoDiscovery)
    {
        _workItemDiscovery = workItemDiscovery;
        _projectDiscovery = projectDiscovery;
        _repoDiscovery = repoDiscovery;
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
        return new MigrationOptions
        {
            Policies = new MigrationPoliciesOptions
            {
                Retries = new MigrationRetriesOptions { Max = policies.MaxRetries },
                Throttle = new MigrationThrottleOptions { MaxConcurrency = policies.MaxConcurrency },
                Checkpoints = new MigrationCheckpointsOptions { Interval = policies.CheckpointIntervalSeconds }
            },
            Organisations = organisations.Select(o =>
            {
                var ado = o.Endpoint as AzureDevOpsEndpointOptions;
                return new AzureDevOpsOrganisationEntry
                {
                    Type = o.Endpoint.Type,
                    Url = ado?.Url ?? o.Endpoint.GetResolvedUrl(),
                    Projects = new System.Collections.Generic.List<string>(o.Projects),
                    ApiVersion = ado?.ApiVersion,
                    Authentication = ado?.Authentication ?? new EndpointAuthenticationOptions(),
                    Enabled = true,
                    Scopes = o.Scopes.Select(s => new MigrationOptionsScope
                    {
                        Type = s.Type,
                        Parameters = s.Parameters.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value is JsonElement je
                                ? je
                                : JsonSerializer.SerializeToElement(kvp.Value))
                    }).ToList()
                };
            }).Cast<DevOpsMigrationPlatform.Abstractions.Options.OrganisationEntry>().ToList()
        };
    }
}
