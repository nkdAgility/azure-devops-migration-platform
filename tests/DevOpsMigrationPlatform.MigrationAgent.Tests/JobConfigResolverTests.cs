// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.MigrationAgent.Tests;

/// <summary>
/// Behavioural tests for <see cref="JobConfigResolver"/> — the use-case service
/// extracted from <c>JobAgentWorker</c> that normalises migration-config.json into
/// org endpoint maps, discovery settings, and the prepare probe payload.
/// </summary>
[TestClass]
public sealed class JobConfigResolverTests
{
    private static JsonSerializerOptions CreateAgentJsonOptions()
    {
        // Mirrors AgentWorkerBase's JSON options: camelCase, case-insensitive,
        // enum-as-string, plus the polymorphic endpoint/organisation converters.
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("Simulated", typeof(SimulatedEndpointOptions));
        registry.RegisterOrganisationEntry("Simulated", typeof(SimulatedOrganisationEntry));

        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(),
                new PolymorphicEndpointOptionsConverter(registry),
                new PolymorphicOrganisationEntryConverter(registry)
            }
        };
    }

    private static JobConfigResolver CreateResolver() => new(CreateAgentJsonOptions());

    private const string SimulatedMultiOrgJson = """
        {
          "MigrationPlatform": {
            "Source": {
              "Type": "Simulated",
              "Generator": { "Projects": [ { "Name": "ProjA" }, { "Name": "ProjB" } ] }
            },
            "Policies": {
              "Retries": { "Max": 7 },
              "Throttle": { "MaxConcurrency": 3 },
              "Checkpoints": { "Interval": 120 }
            },
            "Organisations": [
              { "Type": "Simulated", "Enabled": true, "Projects": [ "ProjA" ] },
              { "Type": "Simulated", "Enabled": false, "Projects": [ "Skipped" ] }
            ]
          }
        }
        """;

    // ── ResolveOrganisationEndpoints ─────────────────────────────────────────

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveOrganisationEndpoints_NullOrWhitespaceJson_ReturnsEmptyMap()
    {
        var resolver = CreateResolver();

        Assert.AreEqual(0, resolver.ResolveOrganisationEndpoints(null).Count);
        Assert.AreEqual(0, resolver.ResolveOrganisationEndpoints("   ").Count);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveOrganisationEndpoints_SkipsDisabledOrganisations()
    {
        var resolver = CreateResolver();

        var map = resolver.ResolveOrganisationEndpoints(SimulatedMultiOrgJson);

        // Both enabled orgs resolve to the same simulated URL, so one entry;
        // the disabled org must not contribute.
        Assert.AreEqual(1, map.Count);
        Assert.IsTrue(map.ContainsKey("https://simulated.example.com"));
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveOrganisationEndpoints_PropagatesSourceGeneratorIntoSimulatedEntries()
    {
        var options = CreateAgentJsonOptions();
        var resolver = new JobConfigResolver(options);

        // The endpoint map itself does not expose the generator, so verify propagation
        // via ResolveDiscoverySettings which shares the same normalisation rule.
        var resolution = resolver.ResolveDiscoverySettings(SimulatedMultiOrgJson);

        Assert.AreEqual(1, resolution.Organisations.Count);
        var endpoint = resolution.Organisations[0].Endpoint as SimulatedEndpointOptions;
        Assert.IsNotNull(endpoint);
        Assert.AreEqual(2, endpoint.Generator.Projects.Count);
        Assert.AreEqual("ProjA", endpoint.Generator.Projects[0].Name);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveOrganisationEndpoints_DoesNotOverwriteEntryOwnGenerator()
    {
        const string json = """
            {
              "MigrationPlatform": {
                "Source": {
                  "Type": "Simulated",
                  "Generator": { "Projects": [ { "Name": "FromSource" } ] }
                },
                "Organisations": [
                  {
                    "Type": "Simulated",
                    "Enabled": true,
                    "Generator": { "Projects": [ { "Name": "OwnProject" } ] }
                  }
                ]
              }
            }
            """;

        var resolver = CreateResolver();
        var resolution = resolver.ResolveDiscoverySettings(json);

        var endpoint = resolution.Organisations[0].Endpoint as SimulatedEndpointOptions;
        Assert.IsNotNull(endpoint);
        Assert.AreEqual(1, endpoint.Generator.Projects.Count);
        Assert.AreEqual("OwnProject", endpoint.Generator.Projects[0].Name);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveOrganisationEndpoints_MalformedJson_Throws()
    {
        var resolver = CreateResolver();

        Assert.ThrowsExactly<JsonException>(() => resolver.ResolveOrganisationEndpoints("{ not json"));
    }

    // ── ResolveDiscoverySettings ─────────────────────────────────────────────

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveDiscoverySettings_NullJson_ReturnsEmptyOrganisationsAndDefaultPolicies()
    {
        var resolver = CreateResolver();

        var resolution = resolver.ResolveDiscoverySettings(null);

        Assert.AreEqual(0, resolution.Organisations.Count);
        Assert.IsNotNull(resolution.Policies);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveDiscoverySettings_MapsPoliciesFromConfig()
    {
        var resolver = CreateResolver();

        var resolution = resolver.ResolveDiscoverySettings(SimulatedMultiOrgJson);

        Assert.AreEqual(7, resolution.Policies.MaxRetries);
        Assert.AreEqual(3, resolution.Policies.MaxConcurrency);
        Assert.AreEqual(120, resolution.Policies.CheckpointIntervalSeconds);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveDiscoverySettings_MapsProjectsAndSkipsDisabledOrganisations()
    {
        var resolver = CreateResolver();

        var resolution = resolver.ResolveDiscoverySettings(SimulatedMultiOrgJson);

        Assert.AreEqual(1, resolution.Organisations.Count);
        CollectionAssert.AreEqual(new[] { "ProjA" }, resolution.Organisations[0].Projects);
    }

    [TestMethod]
    [TestCategory("L0")]
    public void ResolveDiscoverySettings_MapsScopesToJobModuleScopes()
    {
        const string json = """
            {
              "MigrationPlatform": {
                "Organisations": [
                  {
                    "Type": "Simulated",
                    "Enabled": true,
                    "Scopes": [
                      { "Type": "wiql", "Parameters": { "query": "SELECT [System.Id] FROM WorkItems" } }
                    ]
                  }
                ]
              }
            }
            """;

        var resolver = CreateResolver();
        var resolution = resolver.ResolveDiscoverySettings(json);

        Assert.AreEqual(1, resolution.Organisations.Count);
        var scopes = resolution.Organisations[0].Scopes;
        Assert.AreEqual(1, scopes.Count);
        Assert.AreEqual("wiql", scopes[0].Type);
        Assert.AreEqual("SELECT [System.Id] FROM WorkItems", scopes[0].Parameters["query"]?.ToString());
    }

    // ── BuildPrepareProbePayload ─────────────────────────────────────────────

    [TestMethod]
    [TestCategory("L0")]
    public void BuildPrepareProbePayload_ProducesExpectedShape()
    {
        var resolver = CreateResolver();
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var payload = resolver.BuildPrepareProbePayload("job-123", timestamp);

        using var doc = JsonDocument.Parse(payload);
        Assert.AreEqual("job-123", doc.RootElement.GetProperty("jobId").GetString());
        Assert.AreEqual("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(timestamp, doc.RootElement.GetProperty("timestamp").GetDateTimeOffset());
    }
}
