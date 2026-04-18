using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Factories;

/// <summary>
/// Creates a configured <see cref="IDependencyDiscoveryService"/> from
/// <see cref="ScopedOrganisationEndpoint"/> entries carried on a <see cref="DiscoveryJob"/>.
/// Used by <c>DependencyDiscoveryModule</c> so the agent does not need a config file at runtime.
/// </summary>
public sealed class DependencyDiscoveryServiceFactory : IDependencyDiscoveryServiceFactory
{
    private readonly System.IServiceProvider _serviceProvider;
    private readonly ICatalogService _catalogService;
    private readonly ILogger<DependencyDiscoveryService> _logger;

    public DependencyDiscoveryServiceFactory(
        System.IServiceProvider serviceProvider,
        ICatalogService catalogService,
        ILogger<DependencyDiscoveryService> logger)
    {
        _serviceProvider = serviceProvider;
        _catalogService = catalogService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IDependencyDiscoveryService Create(
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        JobPolicies policies)
    {
        var options = BuildDiscoveryOptions(organisations, policies);
        return new DependencyDiscoveryService(
            new OptionsWrapper<DiscoveryOptions>(options),
            _serviceProvider,
            _catalogService,
            _logger);
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
            Organisations = organisations.Select(o => new OrganisationEntry
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
            }).ToList()
        };
    }
}
