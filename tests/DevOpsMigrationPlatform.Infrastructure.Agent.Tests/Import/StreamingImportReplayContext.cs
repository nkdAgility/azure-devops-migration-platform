// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
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
/// Shared scenario state and mocks for Streaming Import Replay step definitions.
/// </summary>
public class StreamingImportReplayContext
{
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);
    internal Mock<ITestArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> MockPackage { get; }

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    /// <summary>Folder paths returned by EnumerateAsync in order.</summary>
    public List<string> FolderPaths { get; set; } = new();

    /// <summary>Folder paths actually processed (not skipped) by the orchestrator.</summary>
    public List<string> ProcessedFolders { get; } = new();

    /// <summary>Fields received by UpdateFieldsAsync keyed by target work item ID.</summary>
    public List<(int TargetId, IReadOnlyList<WorkItemField> Fields)> AppliedFields { get; } = new();

    public StreamingImportReplayContext()
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

    /// <summary>
    /// Sets up the package mock to enumerate <see cref="FolderPaths"/> and return
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

        MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("revision.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("comment.json")), It.IsAny<CancellationToken>()))
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
