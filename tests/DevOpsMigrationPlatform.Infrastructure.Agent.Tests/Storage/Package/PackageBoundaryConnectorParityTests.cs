// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public class PackageBoundaryConnectorParityTests
{
    [TestMethod]
    public async Task ReadCursorAsync_UsesEquivalentPackageBoundarySemantics_ForAzureDevOpsServices()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "ParityProject";
        var expectedKey = PackagePaths.CursorFile("export", "workitems", endpointUrl, projectName);

        var endpointAccessor = BuildSourceEndpointAccessor(endpointUrl, projectName, "AzureDevOpsServices");
        var package = BuildPackageReturningCursor(expectedKey);
        var sut = new CheckpointingService(new Mock<IStateStore>(MockBehavior.Strict).Object, endpointAccessor.Object, null, null, package.Object);

        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task ReadCursorAsync_UsesEquivalentPackageBoundarySemantics_ForTeamFoundationServer()
    {
        const string endpointUrl = "https://tfs.contoso.local/tfs/DefaultCollection";
        const string projectName = "ParityProject";
        var expectedKey = PackagePaths.CursorFile("export", "workitems", endpointUrl, projectName);

        var endpointAccessor = BuildSourceEndpointAccessor(endpointUrl, projectName, "TeamFoundationServer");
        var package = BuildPackageReturningCursor(expectedKey);
        var sut = new CheckpointingService(new Mock<IStateStore>(MockBehavior.Strict).Object, endpointAccessor.Object, null, null, package.Object);

        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task ReadCursorAsync_UsesEquivalentPackageBoundarySemantics_ForSimulated()
    {
        const string endpointUrl = "";
        const string projectName = "ParityProject";
        const string expectedKey = "simulated/ParityProject/.migration/export.workitems.cursor.json";

        var endpointAccessor = BuildSourceEndpointAccessor(endpointUrl, projectName, "Simulated");
        var package = BuildPackageReturningCursor(expectedKey);
        var sut = new CheckpointingService(new Mock<IStateStore>(MockBehavior.Strict).Object, endpointAccessor.Object, null, null, package.Object);

        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        package.VerifyAll();
    }

    private static Mock<ICurrentJobEndpointAccessor> BuildSourceEndpointAccessor(string url, string project, string connectorType)
    {
        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(url);
        sourceInfo.SetupGet(s => s.Project).Returns(project);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns(connectorType);
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);
        return endpointAccessor;
    }

    private static Mock<IPackage> BuildPackageReturningCursor(string expectedKey)
    {
        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };

        var package = new Mock<IPackage>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestAsync(
                It.Is<PackageContext>(c => c.ContentKind == expectedKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(entry))), "application/json"));
        return package;
    }
}
