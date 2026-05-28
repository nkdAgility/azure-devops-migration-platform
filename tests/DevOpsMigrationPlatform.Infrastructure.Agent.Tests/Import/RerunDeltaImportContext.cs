// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state and mocks for Rerun Delta Import step definitions.
/// Uses a real <see cref="CheckpointingService"/> backed by <see cref="IPackageAccess"/>
/// to test cursor-based resume and ForceFresh cursor deletion.
/// </summary>
public class RerunDeltaImportContext
{
    public const string EndpointUrl = "https://dev.azure.com/contoso";
    public const string ProjectName = "Shop";

    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<ICurrentJobEndpointAccessor> MockEndpointAccessor { get; } = new(MockBehavior.Strict);
    internal Mock<ITestArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Loose);
    internal Mock<ITestStateStore> MockStateStore { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> MockPackage { get; }

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

    /// <summary>Tracks whether cursor meta reads have been configured by a previous step.</summary>
    public bool CursorReadConfigured { get; set; }

    public RerunDeltaImportContext()
    {
        MockPackage = PackageTestFactory.CreateCombinedDelegatingMock(MockArtefactStore.Object, MockStateStore.Object);
        var target = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        target.SetupGet(t => t.Url).Returns(EndpointUrl);
        target.SetupGet(t => t.Project).Returns(ProjectName);
        target.SetupGet(t => t.ConnectorType).Returns("AzureDevOpsServices");

        MockEndpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        MockEndpointAccessor.SetupGet(a => a.Target).Returns(target.Object);

        CheckpointingService = new CheckpointingService(
            currentJobEndpointAccessor: MockEndpointAccessor.Object,
            package: MockPackage.Object);
    }

    public WorkItemOrchestrator BuildOrchestrator()
    {
        var processor = new WorkItemResolutionProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            CheckpointingService,
            (IIdentityLookupTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            EndpointUrl,
            ProjectName,
            package: MockPackage.Object);

        return new WorkItemOrchestrator(
            MockPackage.Object,
            EndpointUrl,
            ProjectName,
            CheckpointingService,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            NullLogger<WorkItemOrchestrator>.Instance);
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
