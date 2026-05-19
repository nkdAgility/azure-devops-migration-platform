// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class LegacyCheckpointPrecedenceTests
{
    [TestMethod]
    public async Task ReadCursorAsync_ProjectScopedPrecedesLegacyRoot()
    {
        var source = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        source.SetupGet(x => x.Url).Returns("https://dev.azure.com/contoso");
        source.SetupGet(x => x.Project).Returns("Shop");
        source.SetupGet(x => x.ConnectorType).Returns("AzureDevOpsServices");
        var endpoints = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpoints.SetupGet(x => x.Source).Returns(source.Object);
        endpoints.SetupGet(x => x.Target).Returns((ITargetEndpointInfo?)null);

        var authoritative = new CursorEntry { LastProcessed = "A", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow };
        var authoritativeJson = JsonSerializer.Serialize(authoritative);
        var projectKey = PackagePathTestHelper.CursorFile("export", "workitems", "https://dev.azure.com/contoso", "Shop");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "export" && c.Module == "workitems"),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(projectKey, new PackageMetaPayload(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(authoritativeJson))))));

        var sut = new CheckpointingService(
            endpoints.Object,
            package: package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("A", result.LastProcessed);
    }
}
