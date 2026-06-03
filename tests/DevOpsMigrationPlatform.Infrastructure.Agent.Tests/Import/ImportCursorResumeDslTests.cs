// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class ImportCursorResumeDslTests
{
    private static readonly string s_revisionJson =
        """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(
        IEnumerable<string> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    private static void SetupFolderEnumeration(ImportCursorResumeContext ctx)
    {
        ctx.MockPackage
            .Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken ct) => ToAsyncEnumerable(ctx.AllFolderPaths, ct));

        foreach (var path in ctx.AllFolderPaths)
        {
            ctx.MockPackage
                .Setup(p => p.RequestContentAsync(
                    It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{path.Replace("WorkItems/", string.Empty)}/revision.json", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(s_revisionJson)), "application/json"));
            ctx.MockPackage
                .Setup(p => p.RequestContentAsync(
                    It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{path.Replace("WorkItems/", string.Empty)}/comment.json", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PackagePayload?)null);
        }
    }

    private static void SetupIdMapNoOp(ImportCursorResumeContext ctx, bool hasExistingMapping = false, int sourceId = 1, int targetId = 10)
    {
        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        if (hasExistingMapping)
            ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>())).ReturnsAsync(targetId);
        else
        {
            ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
            ctx.MockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }
        ctx.MockIdMapStore.Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());
    }

    private static void SetupTargetNoOp(ImportCursorResumeContext ctx, int targetId = 10, bool alreadyMapped = false)
    {
        ctx.MockTarget.Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = targetId, IsNewlyCreated = true });
        ctx.MockTarget.Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        if (!alreadyMapped)
            ctx.MockIdMapStore.SetupSequence(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((int?)null).ReturnsAsync(targetId);
    }

    private static void SetupResolutionStrategyNoOp(ImportCursorResumeContext ctx)
    {
        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    // ── Scenario: Interrupted import resumes from the last cursor position ─────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_WithCursor_SkipsFoldersAtOrBeforeCursorAndResumesAfter()
    {
        var ctx = new ImportCursorResumeContext();
        ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            "WorkItems/2024-01-01/00000638000000000002-1-1",
            "WorkItems/2024-01-01/00000638000000000003-1-2",
        };
        SetupFolderEnumeration(ctx);
        SetupIdMapNoOp(ctx);
        SetupTargetNoOp(ctx);
        SetupResolutionStrategyNoOp(ctx);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var cursorJson = JsonSerializer.Serialize(new CursorEntry
        {
            LastProcessed = ctx.AllFolderPaths[1],
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        ctx.MockPackage
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && c.Module == "workitems"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageMetaResult(
                PackagePathTestHelper.CursorFile("import", "workitems", ImportCursorResumeContext.EndpointUrl, ImportCursorResumeContext.ProjectName),
                new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes(cursorJson)), "application/json")));

        var orchestrator = ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Scenario: Mid-folder resume continues from the interrupted stage ───────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_MidFolderCursor_SkipsCompletedStagesAndContinuesFromAppliedLinks()
    {
        var ctx = new ImportCursorResumeContext();
        ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
        };
        SetupFolderEnumeration(ctx);
        SetupIdMapNoOp(ctx, hasExistingMapping: true, sourceId: 1, targetId: 10);
        SetupTargetNoOp(ctx, alreadyMapped: true);
        SetupResolutionStrategyNoOp(ctx);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var cursorJson = JsonSerializer.Serialize(new CursorEntry
        {
            LastProcessed = ctx.AllFolderPaths[0],
            Stage = "AppliedFields",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        ctx.MockPackage
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && c.Module == "workitems"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageMetaResult(
                PackagePathTestHelper.CursorFile("import", "workitems", ImportCursorResumeContext.EndpointUrl, ImportCursorResumeContext.ProjectName),
                new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes(cursorJson)), "application/json")));

        var orchestrator = ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // CreateWorkItemAsync must NOT be called (already mapped)
        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // AddLinksAsync (AppliedLinks stage) must be called
        ctx.MockTarget.Verify(
            t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Scenario: Force-fresh deletes the cursor but preserves the ID map ──────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_ForceFresh_DeletesCursorButPreservesIdMap()
    {
        var ctx = new ImportCursorResumeContext();
        ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
        };
        SetupFolderEnumeration(ctx);
        SetupIdMapNoOp(ctx);
        SetupTargetNoOp(ctx);
        SetupResolutionStrategyNoOp(ctx);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var cursorJson = JsonSerializer.Serialize(new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000638000000000001-1-0",
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        ctx.MockPackage
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && c.Module == "workitems"),
                It.IsAny<CancellationToken>()))
            .Returns((PackageMetaContext _, CancellationToken _) => ValueTask.FromResult(new PackageMetaResult(
                PackagePathTestHelper.CursorFile("import", "workitems", ImportCursorResumeContext.EndpointUrl, ImportCursorResumeContext.ProjectName),
                ctx.CursorWasDeleted ? null : new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes(cursorJson)), "application/json"))));

        var orchestrator = ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.ForceFresh, CancellationToken.None);

        Assert.IsTrue(ctx.CursorWasDeleted, "Cursor should have been deleted for ForceFresh.");
        // idmap.db not touched
        ctx.MockPackage.Verify(
            p => p.ResetMetaAsync(It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Module == "idmap"), It.IsAny<CancellationToken>()),
            Times.Never);
        // Processing started from beginning
        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
