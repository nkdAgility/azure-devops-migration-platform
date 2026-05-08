// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state for Import Cursor Resume step definitions.
/// </summary>
public class ImportCursorResumeContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<IStateStore> MockStateStore { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);

    public CheckpointingService CheckpointingService { get; }
    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    /// <summary>All folders in the package.</summary>
    public List<string> AllFolderPaths { get; set; } = new();

    /// <summary>Folder paths actually fed to RevisionFolderProcessor.</summary>
    public List<string> ProcessedFolders { get; } = new();

    /// <summary>The cursor that was deleted (captured by DeleteCursorAsync).</summary>
    public bool CursorWasDeleted { get; set; }

    /// <summary>Stages that were skipped for the mid-folder resume scenario.</summary>
    public List<string> SkippedStages { get; } = new();

    public ImportCursorResumeContext()
    {
        CheckpointingService = new CheckpointingService(MockStateStore.Object);
        MockStateStore
            .Setup(s => s.ReadAsync(PackagePaths.CursorFile("workitems"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        MockStateStore
            .Setup(s => s.DeleteAsync(PackagePaths.CursorFile("workitems"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            CheckpointingService,
            (IIdentityLookupTool?)null,
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
}
