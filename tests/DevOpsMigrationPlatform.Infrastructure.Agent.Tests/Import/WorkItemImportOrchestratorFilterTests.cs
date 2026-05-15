// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemImportOrchestratorFilterTests
{
    private Mock<ICheckpointingService> _mockCps = null!;
    private Mock<IProgressSink> _mockProgress = null!;
    private Mock<IWorkItemResolutionStrategy> _mockStrategy = null!;
    private Mock<IIdMapStore> _mockIdMap = null!;
    private Mock<IWorkItemImportTarget> _mockTarget = null!;
    private Mock<IPackageAccess> _mockPackage = null!;
    private List<string> _folders = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockCps = new Mock<ICheckpointingService>(MockBehavior.Loose);
        _mockProgress = new Mock<IProgressSink>(MockBehavior.Loose);
        _mockStrategy = new Mock<IWorkItemResolutionStrategy>(MockBehavior.Loose);
        _mockIdMap = new Mock<IIdMapStore>(MockBehavior.Loose);
        _mockTarget = new Mock<IWorkItemImportTarget>(MockBehavior.Loose);
        _mockPackage = PackageTestFactory.CreateLooseMock();
        _folders = new List<string>();

        _mockCps.Setup(s => s.ReadCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        _mockIdMap.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockIdMap.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((int id, CancellationToken _) => id);
        _mockIdMap.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockIdMap.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Array.Empty<IdMapEntry>());
        _mockIdMap.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((int?)null);
        _mockIdMap.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _mockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);

        _mockPackage.Setup(p => p.EnumerateContentAsync(
                            It.Is<PackageContentContext>(c =>
                                c.IsCollectionRequest &&
                                string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase)),
                            It.IsAny<CancellationToken>()))
                    .Returns((PackageContentContext _, CancellationToken ct) => ToAsyncEnumerable(_folders, ct));

        _mockPackage.Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()))
                    .Returns((PackageContentContext context, CancellationToken _) => ToPayload(DefaultRevisionJson(context.Address?.RelativePath ?? string.Empty)));
    }

    // ── filter pre-pass ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_WithMatchingFilter_ImportsOnlyPassingWorkItems()
    {
        // wi 1: AreaPath = "MyOrg\TeamA" — matches include filter
        // wi 2: AreaPath = "MyOrg\Archived" — does not match
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 1, revIndex: 1, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\Archived");
        AddRevisionFolder(wiId: 2, revIndex: 1, areaPath: @"MyOrg\Archived");

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.AreaPath", FilterOperator.Regex, @"^MyOrg\\TeamA")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.UpdateFieldsAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "Work item 1 should be imported.");
        _mockTarget.Verify(
            t => t.UpdateFieldsAsync(2, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never, "Work item 2 should be skipped.");
    }

    [TestMethod]
    public async Task ImportAsync_WithNoFilter_ImportsAllWorkItems()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\Archived");

        var orchestrator = BuildOrchestrator(filterOptions: null);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "Both work items should be imported when no filter is configured.");
    }

    [TestMethod]
    public async Task ImportAsync_ExcludeFilter_SkipsMatchingItem()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\Archived");

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.AreaPath", FilterOperator.NotRegex, @"^MyOrg\\Archived")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.UpdateFieldsAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "Work item 1 should not be excluded.");
        _mockTarget.Verify(
            t => t.UpdateFieldsAsync(2, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never, "Work item 2 should be excluded by NotRegex filter.");
    }

    [TestMethod]
    public async Task ImportAsync_FilterEvaluatesLastRevisionOnly()
    {
        // Add early revision with State=Closed, then latest with State=Active
        // Filter includes Active — should import the item
        AddRevisionFolderWithState(wiId: 1, revIndex: 0, state: "Closed");
        AddRevisionFolderWithState(wiId: 1, revIndex: 1, state: "Active");

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.State", FilterOperator.Regex, "^Active$")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.UpdateFieldsAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "Work item 1 should be imported based on its latest revision.");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private WorkItemImportOrchestrator BuildOrchestrator(
        IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions = null)
    {
        var processor = new RevisionFolderProcessor(
            _mockTarget.Object,
            _mockIdMap.Object,
            _mockCps.Object,
            (IIdentityLookupTool?)null,
            NullLogger<RevisionFolderProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            package: _mockPackage.Object);

        return new WorkItemImportOrchestrator(
            _mockPackage.Object,
            "https://dev.azure.com/contoso",
            "Shop",
            _mockCps.Object,
            _mockProgress.Object,
            _mockStrategy.Object,
            _mockIdMap.Object,
            processor,
            _mockTarget.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance,
            filterOptions: filterOptions);
    }

    private void AddRevisionFolder(int wiId, int revIndex, string areaPath)
    {
        var ticks = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).Ticks.ToString("D20");
        var date = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).ToString("yyyy-MM-dd");
        var folder = $"WorkItems/{date}/{ticks}-{wiId}-{revIndex}/";
        _folders.Add(folder);

        var json = BuildRevisionJson(wiId, revIndex, new[]
        {
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" },
            new WorkItemField { ReferenceName = "System.AreaPath", Value = areaPath }
        });
        _mockPackage.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.Contains($"{ticks}-{wiId}-{revIndex}", StringComparison.Ordinal) && c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(json));
    }

    private void AddRevisionFolderWithState(int wiId, int revIndex, string state)
    {
        var ticks = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).Ticks.ToString("D20");
        var date = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).ToString("yyyy-MM-dd");
        var folder = $"WorkItems/{date}/{ticks}-{wiId}-{revIndex}/";
        _folders.Add(folder);

        var json = BuildRevisionJson(wiId, revIndex, new[]
        {
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" },
            new WorkItemField { ReferenceName = "System.State", Value = state }
        });
        _mockPackage.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.Contains($"{ticks}-{wiId}-{revIndex}", StringComparison.Ordinal) && c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(json));
    }

    private static string? DefaultRevisionJson(string path)
    {
        // Parse wiId and revIndex from path
        var folder = path.Replace("/revision.json", "").TrimEnd('/');
        var folderName = folder[(folder.LastIndexOf('/') + 1)..];
        var parts = folderName.Split('-');
        int.TryParse(parts.Length >= 2 ? parts[1] : "1", out var wiId);
        int.TryParse(parts.Length >= 3 ? parts[2] : "0", out var revIdx);
        return BuildRevisionJson(wiId, revIdx, new[]
        {
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" }
        });
    }

    private static string BuildRevisionJson(int wiId, int revIndex, WorkItemField[] fields)
    {
        var fieldsJson = JsonSerializer.Serialize(
            Array.ConvertAll(fields, f => new { referenceName = f.ReferenceName, value = f.Value }));
        return $"{{\"WorkItemId\":{wiId},\"RevisionIndex\":{revIndex},\"Fields\":{fieldsJson},\"Attachments\":[],\"RelatedLinks\":[],\"ExternalLinks\":[],\"Hyperlinks\":[],\"EmbeddedImages\":[]}}";
    }

    private static ValueTask<PackagePayload?> ToPayload(string? content)
    {
        if (content is null)
            return ValueTask.FromResult<PackagePayload?>(null);

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false), "application/json"));
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(
        IEnumerable<string> items,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
