// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.Collections.Generic;
using System.Diagnostics;
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
public sealed class CursorUpdateTracingTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task WriteCursor_EmitsStateCursorUpdateSpan()
    {
        var names = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DevOpsMigrationPlatform.Migration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => names.Add(a.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        var endpoints = CreateEndpointAccessor();
        var package = PackageTestFactory.CreateLooseMock();
        var sut = new CheckpointingService(
            endpoints.Object,
            null,
            null,
            package.Object);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry { LastProcessed = "P", Stage = CursorStage.Completed }, CancellationToken.None);
        CollectionAssert.Contains(names, "state.cursor.update");
    }

    private static Mock<ICurrentJobEndpointAccessor> CreateEndpointAccessor()
    {
        var target = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        target.SetupGet(t => t.Url).Returns("https://dev.azure.com/contoso");
        target.SetupGet(t => t.Project).Returns("Shop");
        target.SetupGet(t => t.ConnectorType).Returns("AzureDevOpsServices");

        var endpoints = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpoints.SetupGet(x => x.Source).Returns((ISourceEndpointInfo?)null);
        endpoints.SetupGet(x => x.Target).Returns(target.Object);
        return endpoints;
    }
}
