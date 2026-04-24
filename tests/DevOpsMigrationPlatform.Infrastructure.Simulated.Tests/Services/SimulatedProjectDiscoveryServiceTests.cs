using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Services;

[TestClass]
public sealed class SimulatedProjectDiscoveryServiceTests
{
    [TestMethod]
    public async Task DiscoverProjectsAsync_NoGenerator_ReturnsDefaultProject()
    {
        var service = new SimulatedProjectDiscoveryService();
        var endpoint = new SimulatedEndpointOptions
        {
            Url = "simulated://localhost",
            Type = "Simulated"
        };

        var projects = await service.DiscoverProjectsAsync(endpoint, CancellationToken.None);

        Assert.AreEqual(1, projects.Count);
        Assert.AreEqual("SimulatedProject", projects[0]);
    }

    [TestMethod]
    public async Task DiscoverProjectsAsync_GeneratorWithProjects_ReturnsProjectList()
    {
        var service = new SimulatedProjectDiscoveryService();
        var endpoint = new SimulatedEndpointOptions
        {
            Url = "simulated://localhost",
            Type = "Simulated",
            Generator = new SimulatedGeneratorConfig
            {
                Projects = new List<SimulatedProjectConfig>
                {
                    new() { Name = "Alpha" },
                    new() { Name = "Beta" },
                    new() { Name = "Gamma" }
                }
            }
        };

        var projects = await service.DiscoverProjectsAsync(endpoint, CancellationToken.None);

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
