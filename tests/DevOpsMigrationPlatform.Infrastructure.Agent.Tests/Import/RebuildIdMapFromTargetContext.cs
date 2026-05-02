// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state and mocks for Rebuild ID Map From Target step definitions.
/// </summary>
public class RebuildIdMapFromTargetContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);

    public WorkItemsModuleExtensions Extensions { get; set; } = new();

    /// <summary>In-memory simulation of idmap.db work item mappings (INSERT OR IGNORE semantics).</summary>
    public Dictionary<int, int> IdMap { get; } = new();

    /// <summary>Entries that the resolution strategy will attempt to seed.</summary>
    public List<IdMapEntry> SeedEntries { get; set; } = new();

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            (IIdentityLookupTool?)null,
            MockArtefactStore.Object,
            NullLogger<RevisionFolderProcessor>.Instance);

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

    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> items,
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
