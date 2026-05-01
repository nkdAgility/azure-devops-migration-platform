using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
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
    public void Constructor_WithSimulatedEndpointInfo_ConstructsSuccessfully()
    {
        // Post-refactor: factories no longer validate connector type — they accept ITargetEndpointInfo
        // and the DI container is responsible for registering the correct factory per connector.
        // This test now verifies that construction succeeds with any ITargetEndpointInfo.
        var mockClientFactory = new Mock<IAzureDevOpsClientFactory>();
        var mockEndpointInfo = new Mock<ITargetEndpointInfo>();
        mockEndpointInfo.SetupGet(x => x.ConnectorType).Returns("Simulated");
        mockEndpointInfo.SetupGet(x => x.Url).Returns("https://simulated");
        mockEndpointInfo.SetupGet(x => x.Project).Returns("TestProject");

        var factory = new AzureDevOpsWorkItemImportTargetFactory(
            mockClientFactory.Object,
            mockEndpointInfo.Object);

        Assert.IsNotNull(factory);
    }

    [TestMethod]
    public async Task CreateAsync_WithValidEndpointInfo_CreatesTarget()
    {
        // Arrange
        var mockClientFactory = new Mock<IAzureDevOpsClientFactory>();
        var mockEndpointInfo = new Mock<ITargetEndpointInfo>();
        mockEndpointInfo.SetupGet(x => x.ConnectorType).Returns("AzureDevOpsServices");
        mockEndpointInfo.SetupGet(x => x.Url).Returns("https://dev.azure.com/test");
        mockEndpointInfo.SetupGet(x => x.Project).Returns("TestProject");

        var mockWitClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/test"), null! });
        mockClientFactory
            .Setup(c => c.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockWitClient.Object);

        var factory = new AzureDevOpsWorkItemImportTargetFactory(
            mockClientFactory.Object,
            mockEndpointInfo.Object);

        // Act
        var target = await factory.CreateAsync(CancellationToken.None);

        // Assert
        Assert.IsNotNull(target);
    }
}

