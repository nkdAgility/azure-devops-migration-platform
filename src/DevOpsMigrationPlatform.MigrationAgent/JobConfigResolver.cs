// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Use-case service that normalises job configuration read from the package
/// (<c>.migration/migration-config.json</c>) into the shapes the worker needs:
/// per-organisation endpoint maps, scoped discovery settings, and the prepare
/// probe payload. Extracted from <see cref="JobAgentWorker"/> so the
/// config-resolution behaviour is unit-testable in isolation.
/// </summary>
public interface IJobConfigResolver
{
    /// <summary>
    /// Parses the raw migration-config JSON and builds the org endpoint map used by
    /// inventory jobs, propagating <c>Source.Generator</c> into simulated organisation
    /// entries that do not define their own project generator.
    /// Returns an empty map when the JSON is absent or defines no enabled organisations.
    /// </summary>
    IReadOnlyDictionary<string, OrganisationEndpoint> ResolveOrganisationEndpoints(string? rawJson);

    /// <summary>
    /// Parses the raw migration-config JSON into the scoped organisation list and job
    /// policies used by discovery jobs, propagating <c>Source.Generator</c> into
    /// simulated organisation entries that do not define their own project generator.
    /// </summary>
    DiscoveryConfigResolution ResolveDiscoverySettings(string? rawJson);

    /// <summary>
    /// Builds the JSON payload written to <c>.migration/prepare-probe.json</c>
    /// at the start of the prepare phase.
    /// </summary>
    string BuildPrepareProbePayload(string jobId, DateTimeOffset timestamp);
}

/// <summary>Result of <see cref="IJobConfigResolver.ResolveDiscoverySettings"/>.</summary>
public sealed class DiscoveryConfigResolution
{
    public List<ScopedOrganisationEndpoint> Organisations { get; init; } = new();
    public JobPolicies Policies { get; init; } = new();
}

/// <inheritdoc cref="IJobConfigResolver"/>
public sealed class JobConfigResolver : IJobConfigResolver
{
    private readonly JsonSerializerOptions _jsonOptions;

    public JobConfigResolver(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
    }

    public IReadOnlyDictionary<string, OrganisationEndpoint> ResolveOrganisationEndpoints(string? rawJson)
    {
        var endpointsByUrl = new Dictionary<string, OrganisationEndpoint>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawJson))
            return endpointsByUrl;

        var wrapper = JsonSerializer.Deserialize<DiscoveryConfigWrapper>(rawJson, _jsonOptions);
        if (wrapper?.MigrationPlatform?.Organisations is { Count: > 0 } orgs)
        {
            // Propagate Source.Generator into SimulatedOrganisationEntry when absent.
            var sourceGenerator = (wrapper.MigrationPlatform.Source as SimulatedEndpointOptions)?.Generator;
            foreach (var o in orgs.Where(o => o.Enabled))
            {
                PropagateSourceGenerator(o, sourceGenerator);
                var ep = o.ToEndpointOptions().ToOrganisationEndpoint();
                endpointsByUrl[ep.ResolvedUrl] = ep;
            }
        }

        return endpointsByUrl;
    }

    public DiscoveryConfigResolution ResolveDiscoverySettings(string? rawJson)
    {
        var organisations = new List<ScopedOrganisationEndpoint>();
        var policies = new JobPolicies();
        if (string.IsNullOrWhiteSpace(rawJson))
            return new DiscoveryConfigResolution { Organisations = organisations, Policies = policies };

        var wrapper = JsonSerializer.Deserialize<DiscoveryConfigWrapper>(rawJson, _jsonOptions);
        if (wrapper?.MigrationPlatform?.Organisations is { Count: > 0 } orgs)
        {
            // For Simulated orgs, the Generator lives in Source.Generator (not on each org entry).
            // Propagate it so the discovery service has project definitions to count from.
            var sourceGenerator = (wrapper.MigrationPlatform.Source as SimulatedEndpointOptions)?.Generator;

            organisations = orgs
                .Where(o => o.Enabled)
                .Select(o =>
                {
                    PropagateSourceGenerator(o, sourceGenerator);
                    return new ScopedOrganisationEndpoint
                    {
                        Endpoint = o.ToEndpointOptions(),
                        Projects = new List<string>(o.Projects),
                        Scopes = o.Scopes.Select(s => new JobModuleScope
                        {
                            Type = s.Type,
                            Parameters = s.Parameters.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)
                        }).ToList()
                    };
                })
                .ToList();
        }
        if (wrapper?.MigrationPlatform?.Policies is { } p)
            policies = new JobPolicies { MaxRetries = p.Retries.Max, MaxConcurrency = p.Throttle.MaxConcurrency, CheckpointIntervalSeconds = p.Checkpoints.Interval };

        return new DiscoveryConfigResolution { Organisations = organisations, Policies = policies };
    }

    public string BuildPrepareProbePayload(string jobId, DateTimeOffset timestamp)
        => JsonSerializer.Serialize(new
        {
            jobId,
            timestamp,
            status = "ok"
        });

    private static void PropagateSourceGenerator(OrganisationEntry entry, SimulatedGeneratorConfig? sourceGenerator)
    {
        if (entry is SimulatedOrganisationEntry sim
            && sourceGenerator?.Projects is { Count: > 0 }
            && (sim.Generator?.Projects is null or { Count: 0 }))
        {
            sim.Generator = sourceGenerator;
        }
    }

    internal sealed class DiscoveryConfigWrapper
    {
        public MigrationPlatformOptions? MigrationPlatform { get; set; }
    }
}
