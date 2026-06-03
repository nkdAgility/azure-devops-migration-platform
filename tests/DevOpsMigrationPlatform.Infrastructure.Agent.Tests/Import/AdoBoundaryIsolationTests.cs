// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class AdoBoundaryIsolationTests
{
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ResolutionStrategyFactory_CreateAsync_Throws_WhenTargetIsSimulated()
    {
        // Arrange — ADO resolution factory given a SimulatedWorkItemTarget
        var mockClientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var mockLogger = NullLogger<TargetFieldResolutionStrategy>.Instance;
        var factory = new AzureDevOpsResolutionStrategyFactory(mockClientFactory.Object, mockLogger);

        var simulatedTarget = new SimulatedWorkItemTarget();
        var options = new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "Custom.SourceId" };
        var mockEndpoint = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        mockEndpoint.Setup(e => e.ToOrganisationEndpoint()).Returns(new OrganisationEndpoint());
        mockEndpoint.SetupGet(e => e.Project).Returns("Shop");
        mockEndpoint.SetupGet(e => e.Url).Returns("https://dev.azure.com/contoso");
        mockEndpoint.SetupGet(e => e.ConnectorType).Returns("AzureDevOpsServices");

        // Act & Assert — must throw because target is not AzureDevOpsWorkItemTarget
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => factory.CreateAsync(options, simulatedTarget, mockEndpoint.Object, CancellationToken.None));
    }

    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ResolutionStrategyFactory_CreateAsync_Throws_WhenStrategyNameIsEmpty()
    {
        // Arrange — invalid (empty) strategy value; exception fires before target-type check
        var mockClientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var mockLogger = NullLogger<TargetFieldResolutionStrategy>.Instance;
        var factory = new AzureDevOpsResolutionStrategyFactory(mockClientFactory.Object, mockLogger);

        var mockTarget = new Mock<IWorkItemTarget>(MockBehavior.Loose).Object;
        var options = new WorkItemResolutionStrategyOptions { Strategy = "" };
        var mockEndpoint = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);

        // Act & Assert — throws before any ADO call
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => factory.CreateAsync(options, mockTarget, mockEndpoint.Object, CancellationToken.None));
    }
}

