using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Verifies ADO assembly boundary isolation — the ADO factory must not accept Simulated endpoints.
/// </summary>
[TestClass]
public sealed class AzureDevOpsWorkItemImportTargetFactoryBoundaryTests
{
    [TestMethod]
    public async Task CreateAsync_WithSimulatedEndpointOptions_ThrowsArgumentException()
    {
        var mockClientFactory = new Mock<IAzureDevOpsClientFactory>();
        var factory = new AzureDevOpsWorkItemImportTargetFactory(mockClientFactory.Object);

        var simulatedEndpoint = new SimulatedEndpointOptions();
        simulatedEndpoint.Type = "Simulated";

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => factory.CreateAsync(simulatedEndpoint, CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateAsync_WithUnknownEndpointType_ThrowsArgumentException()
    {
        var mockClientFactory = new Mock<IAzureDevOpsClientFactory>();
        var factory = new AzureDevOpsWorkItemImportTargetFactory(mockClientFactory.Object);

        var unknownEndpoint = new StubEndpointOptions { Type = "SomeOtherConnector" };
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => factory.CreateAsync(unknownEndpoint, CancellationToken.None));
    }

    private sealed class StubEndpointOptions : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint() => new() { Type = Type };
    }
}

