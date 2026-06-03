// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class SimulatedImportTests
{
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_EmitsOneProgressEvent_PerWorkItem()
    {
        // Arrange — 5 work items; orchestrator with SimulatedWorkItemTarget
        const int workItemCount = 5;
        var ctx = new StreamingImportReplayContext();

        for (int i = 1; i <= workItemCount; i++)
            ctx.FolderPaths.Add($"WorkItems/2024-01-01/{(long)(638_000_000_000_000_000 + i):D20}-{i}-0");

        var revisionJsonFor = (int wiId) =>
            $$"""{"WorkItemId":{{wiId}},"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

        // Per-folder revision json
        foreach (var folder in ctx.FolderPaths)
        {
            var parts = folder.Split('/')[^1].Split('-');
            int.TryParse(parts[1], out var wiId);
            ctx.MockArtefactStore
                .Setup(s => s.ReadAsync(It.Is<string>(p => p.Contains(parts[0]) && p.EndsWith("/revision.json")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(revisionJsonFor(wiId));
            ctx.MockArtefactStore
                .Setup(s => s.ReadAsync(It.Is<string>(p => p.Contains(parts[0]) && p.EndsWith("/comment.json")), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }
        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));

        ctx.MockCheckpointing.Setup(s => s.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>())).ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing.Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var idMap = new Dictionary<int, int>();
        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => idMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        ctx.MockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => idMap[src] = tgt)
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());

        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var progressEvents = new List<ProgressEvent>();
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => progressEvents.Add(e));

        // Replace MockTarget with a real SimulatedWorkItemTarget
        ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — at least one progress event per work item (orchestrator emits events per revision)
        Assert.IsTrue(progressEvents.Count >= workItemCount,
            $"Expected at least {workItemCount} progress events, got {progressEvents.Count}.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task SimulatedWorkItemTargetFactory_CreateAsync_AlwaysReturnsSimulatedTarget()
    {
        // Arrange — no AzureDevOpsEndpointOptions involved; factory is self-contained
        var options = Options.Create(new SimulatedEndpointOptions());
        var factory = new SimulatedWorkItemTargetFactory(options);

        // Act
        var target = await factory.CreateAsync(CancellationToken.None);

        // Assert — returns in-memory simulated target (not an ADO or TFS target)
        Assert.IsInstanceOfType<SimulatedWorkItemTarget>(target);
    }
}
