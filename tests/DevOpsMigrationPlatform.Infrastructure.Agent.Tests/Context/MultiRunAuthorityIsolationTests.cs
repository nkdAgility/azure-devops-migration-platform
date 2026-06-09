// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class MultiRunAuthorityIsolationTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ActionQualifiedCursors_DoNotCollideAcrossExportAndImport()
    {
        var package = PackageTestFactory.CreateLooseMock();
        var source = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        source.SetupGet(x => x.Url).Returns("https://dev.azure.com/contoso");
        source.SetupGet(x => x.Project).Returns("Shop");
        source.SetupGet(x => x.ConnectorType).Returns("AzureDevOpsServices");
        var endpoints = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpoints.SetupGet(x => x.Source).Returns(source.Object);
        endpoints.SetupGet(x => x.Target).Returns((ITargetEndpointInfo?)null);

        var sut = new CheckpointingService(
            endpoints.Object,
            package: package.Object);
        await sut.WriteCursorAsync("export.workitems", new CursorEntry { LastProcessed = "E", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry { LastProcessed = "I", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);

        var export = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);
        var import = await sut.ReadCursorAsync("import.workitems", CancellationToken.None);
        Assert.AreEqual("E", export?.LastProcessed);
        Assert.AreEqual("I", import?.LastProcessed);
    }

}
