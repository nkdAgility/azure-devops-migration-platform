// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class FilterScopeImportTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FilterScopeImportContext BuildContext()
    {
        var ctx = new FilterScopeImportContext();
        SetupNoOpMocks(ctx);
        return ctx;
    }

    private static void SetupNoOpMocks(FilterScopeImportContext ctx)
    {
        ctx.MockCheckpointing.Setup(s => s.ReadCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing.Setup(s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockCheckpointing.Setup(s => s.DeleteCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int id, CancellationToken _) => id);
        ctx.MockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        ctx.MockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        ctx.MockPackage
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.IsCollectionRequest && string.Equals(c.Module, "WorkItems", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken ct) =>
                FilterScopeImportContext.ToAsyncEnumerable(ctx.FolderPaths, ct));

        ctx.MockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath != null && c.Address.RelativePath.EndsWith("revision.json")),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath;
                var folderName = GetFolderName(path.Replace("/revision.json", ""));
                var parts = folderName.Split('-');
                int.TryParse(parts.Length >= 2 ? parts[1] : "0", out var wiId);
                int.TryParse(parts.Length >= 3 ? parts[2] : "0", out var revIdx);
                var json = BuildRevisionJson(wiId, revIdx);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))));
            });
    }

    private static void AddRevisionFolder(FilterScopeImportContext ctx, int wiId, int revIndex, WorkItemField[] fields)
    {
        var ticks = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).Ticks.ToString("D20");
        var date = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).ToString("yyyy-MM-dd");
        var folder = $"WorkItems/{date}/{ticks}-{wiId}-{revIndex}/";
        ctx.FolderPaths.Add(folder);

        var json = BuildRevisionJsonWithFields(wiId, revIndex, fields);
        ctx.MockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath != null && c.Address.RelativePath.Contains($"{ticks}-{wiId}-{revIndex}") && c.Address.RelativePath.EndsWith("revision.json")),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) =>
                ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))));
    }

    private static string GetFolderName(string path)
    {
        var last = path.LastIndexOf('/');
        return last >= 0 ? path[(last + 1)..] : path;
    }

    private static string BuildRevisionJson(int wiId, int revIndex)
        => $"{{\"WorkItemId\":{wiId},\"RevisionIndex\":{revIndex},\"Fields\":[{{\"ReferenceName\":\"System.WorkItemType\",\"Value\":\"Task\"}}],\"Attachments\":[],\"RelatedLinks\":[],\"ExternalLinks\":[],\"Hyperlinks\":[],\"EmbeddedImages\":[]}}";

    private static string BuildRevisionJsonWithFields(int wiId, int revIndex, WorkItemField[] fields)
    {
        var fieldJson = System.Text.Json.JsonSerializer.Serialize(
            fields.Select(f => new { referenceName = f.ReferenceName, value = f.Value }));
        return $"{{\"WorkItemId\":{wiId},\"RevisionIndex\":{revIndex},\"Fields\":{fieldJson},\"Attachments\":[],\"RelatedLinks\":[],\"ExternalLinks\":[],\"Hyperlinks\":[],\"EmbeddedImages\":[]}}";
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_ImportsOnlyMatchingWorkItems_WhenIncludeFilterConfigured()
    {
        // Arrange
        var ctx = BuildContext();
        ctx.FilterOptions.Add(new WorkItemFieldFilterOptions("System.AreaPath", FilterOperator.Regex, @"^MyOrg\\TeamA"));

        AddRevisionFolder(ctx, wiId: 1, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\TeamA\Feature1" } });
        AddRevisionFolder(ctx, wiId: 2, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\Archived\Bug1" } });
        AddRevisionFolder(ctx, wiId: 3, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\TeamA\Bug2" } });

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — 1 and 3 imported, 2 skipped
        ctx.MockTarget.Verify(t => t.ApplyRevisionAsync(It.Is<int>(id => id == 1 || id == 3), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        ctx.MockTarget.Verify(t => t.ApplyRevisionAsync(It.Is<int>(id => id == 2), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_EvaluatesLastRevisionOnly_WhenFilteringWorkItems()
    {
        // Arrange — earlier revision is Closed, last is Active; filter includes Active
        var ctx = BuildContext();
        ctx.FilterOptions.Add(new WorkItemFieldFilterOptions("System.State", FilterOperator.Regex, "Active"));

        AddRevisionFolder(ctx, wiId: 1, revIndex: 0, fields: new[] { new WorkItemField { ReferenceName = "System.State", Value = "Closed" } });
        AddRevisionFolder(ctx, wiId: 1, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.State", Value = "Active" } });

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — work item 1 imported because its LAST revision is Active
        ctx.MockTarget.Verify(t => t.ApplyRevisionAsync(It.Is<int>(id => id == 1), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_CompletesWithoutError_WhenSkippedItemHasDiagnosticEntry()
    {
        // Arrange — work item 2 should be skipped; production code writes a diagnostic log entry
        var ctx = BuildContext();
        ctx.FilterOptions.Add(new WorkItemFieldFilterOptions("System.AreaPath", FilterOperator.Regex, @"^MyOrg\\TeamA"));

        AddRevisionFolder(ctx, wiId: 2, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\Archived\Bug1" } });

        // Act — must complete without throwing
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — work item 2 not imported
        ctx.MockTarget.Verify(t => t.ApplyRevisionAsync(It.Is<int>(id => id == 2), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_CompletesSuccessfully_WhenZeroWorkItemsPassFilter()
    {
        // Arrange — filter pattern matches nothing
        var ctx = BuildContext();
        ctx.FilterOptions.Add(new WorkItemFieldFilterOptions("System.AreaPath", FilterOperator.Regex, "^NoMatchingAreaPath$"));

        AddRevisionFolder(ctx, wiId: 1, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\TeamA\Feature1" } });
        AddRevisionFolder(ctx, wiId: 2, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\TeamB\Bug1" } });
        AddRevisionFolder(ctx, wiId: 3, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.AreaPath", Value = @"MyOrg\TeamC\Task1" } });

        // Act — must not throw even though zero items pass the filter
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — no work items imported
        ctx.MockTarget.Verify(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ImportAsync_ImportsAllWorkItems_WhenNoFilterConfigured()
    {
        // Arrange — no filter scopes (backward compatibility)
        var ctx = BuildContext();
        // FilterOptions is empty by default

        AddRevisionFolder(ctx, wiId: 1, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" } });
        AddRevisionFolder(ctx, wiId: 2, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Bug" } });
        AddRevisionFolder(ctx, wiId: 3, revIndex: 1, fields: new[] { new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Feature" } });

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — all 3 imported
        ctx.MockTarget.Verify(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.AtLeast(3));
    }
}


