// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemCadenceTracingTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PackagePathResolution_EmitsStateProgressTrace()
    {
        var spans = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DevOpsMigrationPlatform.Migration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => spans.Add(activity.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        var endpoints = CreateEndpointAccessor();
        var sut = new CheckpointingService(
            endpoints.Object,
            package: PackageTestFactory.CreateLooseMock().Object);
        await sut.WriteContinuationTokenAsync("export.workitems", new BatchContinuationToken(), CancellationToken.None);

        CollectionAssert.Contains(spans, "state.continuation.update");
    }

    private static Mock<ICurrentJobEndpointAccessor> CreateEndpointAccessor()
    {
        var source = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        source.SetupGet(s => s.Url).Returns("https://dev.azure.com/contoso");
        source.SetupGet(s => s.Project).Returns("Shop");
        source.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");

        var endpoints = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpoints.SetupGet(x => x.Source).Returns(source.Object);
        endpoints.SetupGet(x => x.Target).Returns((ITargetEndpointInfo?)null);
        return endpoints;
    }
}
