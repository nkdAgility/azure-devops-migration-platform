// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared state for FilterScopeImport step definitions.
/// </summary>
public class FilterScopeImportContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Loose);
    public Mock<IPackage> MockPackage { get; }

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();
    public List<WorkItemFieldFilterOptions> FilterOptions { get; set; } = new();

    /// <summary>Folder paths returned by EnumerateAsync during the pre-pass and main loop.</summary>
    public List<string> FolderPaths { get; set; } = new();

    /// <summary>Work item IDs actually imported (UpdateFieldsAsync called).</summary>
    public List<int> ImportedWorkItemIds { get; } = new();

    public FilterScopeImportContext()
    {
        MockPackage = PackageTestFactory.CreateDelegatingMock(MockArtefactStore.Object);
    }

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var processorLogger = NullLogger<RevisionFolderProcessor>.Instance;
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            (IIdentityLookupTool?)null,
            MockArtefactStore.Object,
            processorLogger,
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
            filterOptions: FilterOptions.Count > 0 ? FilterOptions : null,
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
            await Task.Yield();
        }
    }
}
