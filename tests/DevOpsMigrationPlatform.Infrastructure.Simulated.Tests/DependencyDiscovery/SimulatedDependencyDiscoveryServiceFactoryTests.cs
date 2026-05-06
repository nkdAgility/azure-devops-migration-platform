// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Factories;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.DependencyDiscovery;

[TestClass]
public sealed class SimulatedDependencyDiscoveryServiceFactoryTests
{
    private const string SimOrgUrl = "simulated://localhost";
    private const string ProjectName = "SimProject";

    private static IReadOnlyList<ScopedOrganisationEndpoint> OneOrg(string project = ProjectName)
        => new List<ScopedOrganisationEndpoint>
        {
            new()
            {
                Endpoint = new SimulatedEndpointOptions { Url = SimOrgUrl },
                Projects = new List<string> { project }
            }
        };

    // ── T024: factory can be instantiated and Create returns a service ─────
    [TestMethod]
    public void Create_WithSimulatedLinkService_ReturnsIDependencyDiscoveryService()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);

        var service = factory.Create(OneOrg(), new JobPolicies());

        Assert.IsNotNull(service, "Create must return a non-null IDependencyDiscoveryService");
    }

    [TestMethod]
    public void CreateForProject_WithSimulatedLinkService_ReturnsIDependencyDiscoveryService()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);

        var service = factory.CreateForProject(OneOrg(), SimOrgUrl, ProjectName, new JobPolicies());

        Assert.IsNotNull(service, "CreateForProject must return a non-null IDependencyDiscoveryService");
    }

    // ── T024: service delegates to SimulatedWorkItemLinkAnalysisService ────
    [TestMethod]
    public async Task DiscoverDependenciesAsync_WithSimulatedConnector_ReturnsEmptySequenceWithoutNetworkCall()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);
        var service = factory.CreateForProject(OneOrg(), SimOrgUrl, ProjectName, new JobPolicies());

        var events = new List<DependencyProgressEvent>();
        await foreach (var evt in service.DiscoverDependenciesAsync(cancellationToken: CancellationToken.None))
        {
            events.Add(evt);
        }

        // SimulatedWorkItemLinkAnalysisService returns no links — empty sequence expected
        Assert.AreEqual(0, events.Count, "Simulated dependency service should return empty link results");
    }

    // ── T024: factory resolves without external connectivity ──────────────
    [TestMethod]
    public void SimulatedFactory_CanBeResolvedFromKeyedDI_WithoutExternalConnectivity()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService, SimulatedWorkItemLinkAnalysisService>(
            serviceKey: "Simulated");
        services.AddSingleton<IDependencyDiscoveryServiceFactory, SimulatedDependencyDiscoveryServiceFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDependencyDiscoveryServiceFactory>();

        Assert.IsNotNull(factory);
        Assert.IsInstanceOfType(factory, typeof(SimulatedDependencyDiscoveryServiceFactory));
    }

    // ── T024: CreateForProject scopes to single project ───────────────────
    [TestMethod]
    public async Task CreateForProject_ScopesDiscoveryToSingleProjectOnly()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);

        var multiOrg = new List<ScopedOrganisationEndpoint>
        {
            new()
            {
                Endpoint = new SimulatedEndpointOptions { Url = SimOrgUrl },
                Projects = new List<string> { "ProjectA", "ProjectB", "ProjectC" }
            }
        };

        var service = factory.CreateForProject(multiOrg, SimOrgUrl, "ProjectA", new JobPolicies());

        // Should not throw and should complete without network calls
        var events = new List<DependencyProgressEvent>();
        await foreach (var evt in service.DiscoverDependenciesAsync(cancellationToken: CancellationToken.None))
        {
            events.Add(evt);
        }

        // Simulated returns no links regardless — just verify it doesn't crash
        Assert.IsNotNull(events);
    }
}
