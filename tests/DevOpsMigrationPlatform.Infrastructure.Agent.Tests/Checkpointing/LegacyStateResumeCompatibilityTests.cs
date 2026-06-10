// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class LegacyStateResumeCompatibilityTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ReadCursorAsync_WhenPackageBoundaryMisses_DoesNotFallBackToLegacyStateStorePath()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "export" && c.Module == "workitems"),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/export.workitems.cursor.json", null)));

        var sut = new CheckpointingService(endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ReadContinuationTokenAsync_WhenPackageBoundaryMisses_DoesNotFallBackToLegacyStateStorePath()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "export" && c.Module == "workitems"),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/export.workitems.continuation.json", null)));

        var sut = new CheckpointingService(endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadContinuationTokenAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
    }
}
