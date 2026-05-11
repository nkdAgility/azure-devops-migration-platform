// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class LegacyStateResumeCompatibilityTests
{
    [TestMethod]
    public async Task ReadCursorAsync_WhenPackageBoundaryMisses_DoesNotFallBackToLegacyStateStorePath()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.CursorFile("export", "workitems", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == expectedKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);

        var sut = new CheckpointingService(stateStore.Object, endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
        stateStore.VerifyAll();
    }

    [TestMethod]
    public async Task ReadContinuationTokenAsync_WhenPackageBoundaryMisses_DoesNotFallBackToLegacyStateStorePath()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.ContinuationFile("export", "workitems", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == expectedKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);

        var sut = new CheckpointingService(stateStore.Object, endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadContinuationTokenAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
        stateStore.VerifyAll();
    }
}
