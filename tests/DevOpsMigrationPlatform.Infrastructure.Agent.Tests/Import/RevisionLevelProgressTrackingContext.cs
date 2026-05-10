// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state and mocks for Revision-Level Progress Tracking step definitions.
/// </summary>
public class RevisionLevelProgressTrackingContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<IPackage> MockPackage { get; }

    public WorkItemsModuleExtensions Extensions { get; set; } = new();

    /// <summary>All folder paths in the package.</summary>
    public List<string> AllFolderPaths { get; set; } = new();

    /// <summary>In-memory watermark dictionary (sourceId → lastRevisionIndex) with MAX semantics.</summary>
    public Dictionary<int, int> Watermarks { get; } = new();

    /// <summary>In-memory idmap dictionary (sourceId → targetId).</summary>
    public Dictionary<int, int> IdMap { get; } = new();

    /// <summary>Revisions that were actually processed (captured via UpdateLastRevisionIndexAsync).</summary>
    public List<(int WorkItemId, int RevisionIndex)> ProcessedRevisions { get; } = new();

    /// <summary>Tracks whether UpdateFieldsAsync was called (and for which target IDs).</summary>
    public List<int> UpdateFieldsCalls { get; } = new();

    /// <summary>Tracks emitted progress events.</summary>
    public List<ProgressEvent> EmittedProgressEvents { get; } = new();

    /// <summary>Tracks whether a comment was created.</summary>
    public List<(int TargetId, string Text)> CreatedComments { get; } = new();

    public RevisionLevelProgressTrackingContext()
    {
        MockPackage = PackageTestFactory.CreateDelegatingMock(MockArtefactStore.Object);
    }

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            (IIdentityLookupTool?)null,
            MockArtefactStore.Object,
            NullLogger<RevisionFolderProcessor>.Instance,
            package: MockPackage.Object);

        return new WorkItemImportOrchestrator(
            MockArtefactStore.Object,
            MockCheckpointing.Object,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance,
            package: MockPackage.Object);
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
