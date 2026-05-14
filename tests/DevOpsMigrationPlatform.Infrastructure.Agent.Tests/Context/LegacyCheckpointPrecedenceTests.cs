// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
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
        var stateStore = new Mock<IStateStore>(MockBehavior.Strict);
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
        stateStore.Setup(x => x.ReadAsync(projectKey, It.IsAny<CancellationToken>())).ReturnsAsync(authoritativeJson);

        var sut = new CheckpointingService(
            stateStore.Object,
            endpoints.Object,
            package: PackageTestFactory.CreateStateDelegatingMock(stateStore.Object).Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("A", result.LastProcessed);
    }
}
