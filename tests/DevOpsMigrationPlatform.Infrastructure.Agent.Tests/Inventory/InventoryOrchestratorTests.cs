// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

[TestClass]
public sealed class InventoryOrchestratorTests
{
    [TestMethod]
    public async Task RunAsync_WhenInventoryCompletes_DoesNotWriteInventoryCompletionMarker()
    {
        var artefactStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var orchestrator = new InventoryOrchestrator(
            NullLogger<InventoryOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/testorg", "TestProject"));
        var context = new InventoryContext
        {
            Job = new Job { JobId = "job-inventory", Kind = JobKind.Inventory },
            ArtefactStore = artefactStore.Object,
            StateStore = stateStore.Object,
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

        stateStore.Verify(
            store => store.WriteAsync(
                PackagePaths.InventoryCompleteFile,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RunAsync_WhenProjectCompletes_WritesProjectScopedInventoryCursor()
    {
        var artefactStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new RecordingStateStore();
        var orchestrator = new InventoryOrchestrator(
            NullLogger<InventoryOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/testorg", "TestProject"));
        var context = new InventoryContext
        {
            Job = new Job { JobId = "job-inventory", Kind = JobKind.Inventory },
            ArtefactStore = artefactStore.Object,
            StateStore = stateStore,
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

        var expectedKey = PackagePaths.CursorFile("inventory", "workitems", "https://dev.azure.com/testorg", "TestProject");
        Assert.IsTrue(stateStore.Writes.ContainsKey(expectedKey), "Inventory must write the authoritative cursor into the project-local .migration folder.");

        var cursor = JsonSerializer.Deserialize<CursorEntry>(stateStore.Writes[expectedKey]);
        Assert.IsNotNull(cursor, "Inventory cursor payload must be a CursorEntry JSON document.");
        Assert.AreEqual(CursorStage.Completed, cursor.Stage);
        Assert.AreEqual("TestProject", cursor.LastProcessed);
        Assert.IsFalse(
            stateStore.Writes.ContainsKey(PackagePaths.CursorFile("inventory.workitems")),
            "Inventory must not write the legacy root cursor key once centralized checkpointing is authoritative.");
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

        return new CheckpointingServiceFactory(endpointAccessor.Object, packageConfigAccessor.Object);
    }

    private sealed class RecordingStateStore : IStateStore
    {
        public Dictionary<string, string> Writes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
        {
            Writes[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Writes.TryGetValue(key, out var value) ? value : null);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Writes.ContainsKey(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Writes.Remove(key);
            return Task.CompletedTask;
        }
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