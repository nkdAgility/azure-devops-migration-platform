// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
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
    public async Task ReadCursorAsync_WhenPackageBoundaryMisses_FallsBackToLegacyStateStorePath()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.CursorFile("export", "workitems", endpointUrl, projectName);
        var expectedEntry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var package = new Mock<IPackage>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestAsync(
                It.Is<PackageContext>(c => c.ContentKind == expectedKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);
        stateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expectedEntry));

        var sut = new CheckpointingService(stateStore.Object, endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedEntry.LastProcessed, result.LastProcessed);
        package.VerifyAll();
        stateStore.VerifyAll();
    }

    [TestMethod]
    public async Task ReadContinuationTokenAsync_WhenPackageBoundaryMisses_FallsBackToLegacyStateStorePath()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.ContinuationFile("export", "workitems", endpointUrl, projectName);
        var expectedToken = new BatchContinuationToken
        {
            ChangedDateUtc = System.DateTime.UtcNow,
            WorkItemId = 7,
            QueryFingerprint = "fingerprint",
            GeneratedAtUtc = System.DateTime.UtcNow
        };

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var package = new Mock<IPackage>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestAsync(
                It.Is<PackageContext>(c => c.ContentKind == expectedKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);
        stateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(expectedToken));

        var sut = new CheckpointingService(stateStore.Object, endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadContinuationTokenAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedToken.WorkItemId, result.WorkItemId);
        Assert.AreEqual(expectedToken.QueryFingerprint, result.QueryFingerprint);
        package.VerifyAll();
        stateStore.VerifyAll();
    }
}
