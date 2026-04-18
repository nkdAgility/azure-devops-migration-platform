using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories;

/// <summary>
/// Creates a configured <see cref="IInventoryService"/> from <see cref="ScopedOrganisationEndpoint"/>
/// entries carried on a <see cref="DiscoveryJob"/>. Used by <c>InventoryDiscoveryModule</c>
/// so the agent does not need a config file at runtime.
/// </summary>
public sealed class InventoryServiceFactory : IInventoryServiceFactory
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
        var options = BuildDiscoveryOptions(organisations, policies);
        return new InventoryService(
            new OptionsWrapper<DiscoveryOptions>(options),
            _workItemDiscovery,
            _projectDiscovery,
            _repoDiscovery);
    }

    private static DiscoveryOptions BuildDiscoveryOptions(
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        JobPolicies policies)
    {
        return new DiscoveryOptions
        {
            Policies = new MigrationPoliciesOptions
            {
                Retries = new MigrationRetriesOptions { Max = policies.MaxRetries },
                Throttle = new MigrationThrottleOptions { MaxConcurrency = policies.MaxConcurrency },
                Checkpoints = new MigrationCheckpointsOptions { Interval = policies.CheckpointIntervalSeconds }
            },
            Organisations = organisations.Select(o => new AzureDevOpsOrganisationEntry
            {
                Type = o.Endpoint.Type,
                Url = o.Endpoint.ResolvedUrl,
                Projects = new System.Collections.Generic.List<string>(o.Projects),
                ApiVersion = o.Endpoint.ApiVersion,
                Authentication = new EndpointAuthenticationOptions
                {
                    Type = o.Endpoint.Authentication.Type,
                    AccessToken = o.Endpoint.Authentication.ResolvedAccessToken
                },
                Enabled = true
            }).Cast<DevOpsMigrationPlatform.Abstractions.Options.OrganisationEntry>().ToList()
        };
    }
}
