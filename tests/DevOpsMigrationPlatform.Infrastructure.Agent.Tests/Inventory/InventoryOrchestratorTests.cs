// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

[TestClass]
public sealed class InventoryOrchestratorTests
{
    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    [TestMethod]
    public async Task RunAsync_WhenInventoryCompletes_DoesNotWriteInventoryCompletionMarker()
    {
        var package = PackageTestFactory.CreateLooseMock();
        var orchestrator = new InventoryOrchestrator(
            NullLogger<InventoryOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/testorg", "TestProject"));
        var context = new InventoryContext
        {
            Job = new Job { JobId = "job-inventory", Kind = JobKind.Inventory },
            Package = package.Object,
            SourceEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = "https://dev.azure.com/testorg",
                Type = "Simulated",
            },
            Project = "TestProject",
        };

        await orchestrator.RunAsync(
            moduleName: "WorkItems",
            eventStream: GetEvents(),
            context: context,
            checkpointIntervalSeconds: 300,
            ct: CancellationToken.None);

        var completionPayload = await package.Object.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress(PackagePathTestHelper.InventoryCompleteFile)),
            CancellationToken.None);
        Assert.IsNull(completionPayload, "Inventory must not write the completion marker.");
    }

    [TestMethod]
    public async Task RunAsync_WhenProjectCompletes_WritesProjectScopedInventoryCursor()
    {
        var package = PackageTestFactory.CreateLooseMock();
        var orchestrator = new InventoryOrchestrator(
            NullLogger<InventoryOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/testorg", "TestProject"));
        var context = new InventoryContext
        {
            Job = new Job { JobId = "job-inventory", Kind = JobKind.Inventory },
            Package = package.Object,
            SourceEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = "https://dev.azure.com/testorg",
                Type = "Simulated",
            },
            Project = "TestProject",
        };

        await orchestrator.RunAsync(
            moduleName: "WorkItems",
            eventStream: GetEvents(),
            context: context,
            checkpointIntervalSeconds: 300,
            ct: CancellationToken.None);

        var metaResult = await package.Object.RequestMetaAsync(
            new PackageMetaContext(PackageMetaKind.CheckpointCursor, Action: "inventory", Module: "workitems"),
            CancellationToken.None);
        Assert.IsTrue(metaResult.Payload is not null, "Inventory must write the authoritative cursor.");
        metaResult.Payload!.Content.Position = 0;
        var cursor = JsonSerializer.Deserialize<CursorEntry>(metaResult.Payload.Content);
        Assert.IsNotNull(cursor);
        Assert.AreEqual(CursorStage.Completed, cursor.Stage);
        Assert.AreEqual("TestProject", cursor.LastProcessed);

        var legacyPayload = await package.Object.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new TestPackageAddress(PackagePathTestHelper.CursorFile("inventory.workitems"))),
            CancellationToken.None);
        Assert.IsNull(legacyPayload, "Inventory must not write the legacy root cursor key.");
    }

    private static ICheckpointingServiceFactory CreateCheckpointingFactory(string endpointUrl, string projectName)
    {
        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("Simulated");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        packageConfigAccessor.SetupGet(a => a.Current).Returns((Microsoft.Extensions.Configuration.IConfiguration?)null);

        return new TestCheckpointingServiceFactory(endpointAccessor.Object, packageConfigAccessor.Object);
    }

    private sealed class TestCheckpointingServiceFactory : ICheckpointingServiceFactory
    {
        private readonly ICurrentJobEndpointAccessor _endpointAccessor;
        private readonly ICurrentPackageConfigAccessor _packageConfigAccessor;

        public TestCheckpointingServiceFactory(
            ICurrentJobEndpointAccessor endpointAccessor,
            ICurrentPackageConfigAccessor packageConfigAccessor)
        {
            _endpointAccessor = endpointAccessor;
            _packageConfigAccessor = packageConfigAccessor;
        }

        public ICheckpointingService Create(IPackageAccess packageAccess)
            => new CheckpointingService(
                _endpointAccessor,
                _packageConfigAccessor,
                null,
                packageAccess);
    }

    private static async IAsyncEnumerable<InventoryProgressEvent> GetEvents([EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new InventoryProgressEvent
        {
            Url = "https://dev.azure.com/testorg",
            ProjectName = "TestProject",
            WorkItemsCount = 12,
            RevisionsCount = 34,
            ReposCount = 2,
            IsComplete = true,
            WindowStart = DateTime.UtcNow.AddDays(-1),
            WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(1),
        };

        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
    }
}
