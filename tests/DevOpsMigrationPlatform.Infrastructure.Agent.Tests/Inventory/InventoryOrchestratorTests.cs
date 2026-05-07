// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

[TestClass]
public sealed class InventoryOrchestratorTests
{
    [TestMethod]
    public async Task RunAsync_WhenInventoryCompletes_WritesInventoryCompletionMarker()
    {
        var artefactStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var orchestrator = new InventoryOrchestrator(NullLogger<InventoryOrchestrator>.Instance);
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
                It.Is<string>(value =>
                    value.Contains("\"phase\":\"Inventory\"", StringComparison.Ordinal) &&
                    value.Contains("\"jobId\":\"job-inventory\"", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
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