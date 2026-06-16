// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state and mocks for Revision-Level Progress Tracking step definitions.
/// </summary>
public class RevisionLevelProgressTrackingContext
{
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);
    internal Mock<ITestArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> MockPackage { get; }

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

    public WorkItemRevisionLoopDriver BuildOrchestrator()
    {
        var processor = new WorkItemResolutionProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            (IIdentityTranslationTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) },
            package: MockPackage.Object);

        return new WorkItemRevisionLoopDriver(new WorkItemRevisionJobScope(
            MockPackage.Object,
            "https://dev.azure.com/contoso",
            "Shop",
            MockCheckpointing.Object,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            JobId: null,
            FilterOptions: null));
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
