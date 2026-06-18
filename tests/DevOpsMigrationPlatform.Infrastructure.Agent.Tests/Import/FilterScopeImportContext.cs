// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
/// Shared state for FilterScopeImport step definitions.
/// </summary>
public class FilterScopeImportContext
{
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> MockPackage { get; }

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();
    public List<WorkItemFieldFilterOptions> FilterOptions { get; set; } = new();

    /// <summary>Folder paths returned by EnumerateAsync during the pre-pass and main loop.</summary>
    public List<string> FolderPaths { get; set; } = new();

    /// <summary>Work item IDs actually imported (UpdateFieldsAsync called).</summary>
    public List<int> ImportedWorkItemIds { get; } = new();

    public FilterScopeImportContext()
    {
        MockPackage = PackageTestFactory.CreateLooseMock();
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
            FilterOptions: FilterOptions.Count > 0 ? (IReadOnlyList<WorkItemFieldFilterOptions>)FilterOptions : null));
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
