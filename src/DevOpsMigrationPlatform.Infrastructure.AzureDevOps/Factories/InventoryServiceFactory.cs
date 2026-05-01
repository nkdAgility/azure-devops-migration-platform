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
            Organisations = organisations.Select(o =>
            {
                var ado = o.Endpoint as AzureDevOpsEndpointOptions;
                var resolved = o.ResolvedEndpoint;

                // Prefer auth from ResolvedEndpoint (carries runtime-resolved PAT from
                // the job's source connector) over the config-time Endpoint auth.
                var auth = resolved is not null
                    ? new EndpointAuthenticationOptions
                    {
                        Type = resolved.Authentication.Type,
                        AccessToken = resolved.Authentication.ResolvedAccessToken ?? string.Empty
                    }
                    : ado?.Authentication ?? new EndpointAuthenticationOptions();

                return new AzureDevOpsOrganisationEntry
                {
                    Type = resolved?.Type ?? o.Endpoint.Type,
                    Url = resolved?.ResolvedUrl ?? ado?.Url ?? o.Endpoint.GetResolvedUrl(),
                    Projects = new System.Collections.Generic.List<string>(o.Projects),
                    ApiVersion = resolved?.ApiVersion ?? ado?.ApiVersion,
                    Authentication = auth,
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
