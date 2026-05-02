// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Services;

[TestClass]
public sealed class SimulatedProjectDiscoveryServiceTests
{
    private static readonly OrganisationEndpoint SimulatedEndpoint = new()
    {
        ResolvedUrl = "simulated://localhost",
        Type = "Simulated",
        Authentication = new OrganisationEndpointAuthentication { Type = AuthenticationType.None }
    };

    [TestMethod]
    public async Task DiscoverProjectsAsync_NoGenerator_ReturnsDefaultProject()
    {
        var service = new SimulatedProjectDiscoveryService();

        var projects = await service.DiscoverProjectsAsync(SimulatedEndpoint, CancellationToken.None);

        Assert.AreEqual(1, projects.Count);
        Assert.AreEqual("SimulatedProject", projects[0]);
    }

    [TestMethod]
    public async Task DiscoverProjectsAsync_GeneratorWithProjects_ReturnsProjectList()
    {
        var generatorConfig = new SimulatedGeneratorConfig
        {
            Projects = new List<SimulatedProjectConfig>
            {
                new() { Name = "Alpha" },
                new() { Name = "Beta" },
                new() { Name = "Gamma" }
            }
        };
        var service = new SimulatedProjectDiscoveryService(generatorConfig);

        var projects = await service.DiscoverProjectsAsync(SimulatedEndpoint, CancellationToken.None);

        Assert.AreEqual(3, projects.Count);
        CollectionAssert.Contains(projects, "Alpha");
        CollectionAssert.Contains(projects, "Beta");
        CollectionAssert.Contains(projects, "Gamma");
    }

    [TestMethod]
    public async Task DiscoverProjectsAsync_NullEndpoint_ReturnsDefault()
    {
        var service = new SimulatedProjectDiscoveryService();
        var projects = await service.DiscoverProjectsAsync(null!, CancellationToken.None);

        Assert.AreEqual(1, projects.Count);
        Assert.AreEqual("SimulatedProject", projects[0]);
    }
}
