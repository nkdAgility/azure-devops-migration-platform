using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state and mocks for Streaming Import Replay step definitions.
/// </summary>
public class StreamingImportReplayContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    /// <summary>Folder paths returned by EnumerateAsync in order.</summary>
    public List<string> FolderPaths { get; set; } = new();

    /// <summary>Folder paths actually processed (not skipped) by the orchestrator.</summary>
    public List<string> ProcessedFolders { get; } = new();

    /// <summary>Fields received by UpdateFieldsAsync keyed by target work item ID.</summary>
    public List<(int TargetId, IReadOnlyList<WorkItemField> Fields)> AppliedFields { get; } = new();

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var processorLogger = NullLogger<RevisionFolderProcessor>.Instance;
        var mockIdentity = new Mock<IIdentityMappingService>(MockBehavior.Loose);
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            mockIdentity.Object,
            MockArtefactStore.Object,
            processorLogger);

        return new WorkItemImportOrchestrator(
            MockArtefactStore.Object,
            MockCheckpointing.Object,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance);
    }

    /// <summary>
    /// Sets up the artefact store mock to enumerate <see cref="FolderPaths"/> and return
    /// a minimal revision.json for each revision folder.
    /// Uses suffix-based matchers instead of per-path setup calls to keep mock registration
    /// O(1) regardless of how many revision folders are in the scenario.
    /// </summary>
    public void SetupArtefactStoreForRevisions(IEnumerable<string> revisionFolderPaths, string? revisionJson = null)
    {
        var json = revisionJson ?? """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

        MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => ToAsyncEnumerable(FolderPaths, ct));

        // Single suffix-based setup covers all paths — avoids O(n) Moq registration cost
        // that would otherwise make large-count scenarios (e.g. 50 000 folders) unusably slow.
        MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/revision.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/comment.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(
        IEnumerable<string> items,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await System.Threading.Tasks.Task.Yield();
        }
    }
}
