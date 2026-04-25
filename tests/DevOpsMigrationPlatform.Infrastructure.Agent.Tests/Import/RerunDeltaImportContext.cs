using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state and mocks for Rerun Delta Import step definitions.
/// Uses a real <see cref="CheckpointingService"/> backed by <see cref="MockStateStore"/>
/// to test cursor-based resume and ForceFresh cursor deletion.
/// </summary>
public class RerunDeltaImportContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<IStateStore> MockStateStore { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);

    public CheckpointingService CheckpointingService { get; }
    public WorkItemsModuleExtensions Extensions { get; set; } = new();

    /// <summary>All folder paths in the package.</summary>
    public List<string> AllFolderPaths { get; set; } = new();

    /// <summary>In-memory watermark dictionary (sourceId → lastRevisionIndex).</summary>
    public Dictionary<int, int> Watermarks { get; } = new();

    /// <summary>In-memory idmap dictionary (sourceId → targetId).</summary>
    public Dictionary<int, int> IdMap { get; } = new();

    /// <summary>Revision indices that were actually processed (captured via UpdateLastRevisionIndexAsync).</summary>
    public List<(int WorkItemId, int RevisionIndex)> ProcessedRevisions { get; } = new();

    /// <summary>Tracks whether the cursor was deleted during ForceFresh.</summary>
    public bool CursorWasDeleted { get; set; }

    /// <summary>Tracks whether MockStateStore.ReadAsync has been configured by a previous step.</summary>
    public bool CursorReadConfigured { get; set; }

    public RerunDeltaImportContext()
    {
        CheckpointingService = new CheckpointingService(MockStateStore.Object);
    }

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var mockIdentity = new Mock<IIdentityMappingService>(MockBehavior.Loose);
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            CheckpointingService,
            mockIdentity.Object,
            MockArtefactStore.Object,
            NullLogger<RevisionFolderProcessor>.Instance);

        return new WorkItemImportOrchestrator(
            MockArtefactStore.Object,
            CheckpointingService,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance);
    }

    public static async IAsyncEnumerable<string> ToAsyncEnumerable(
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
