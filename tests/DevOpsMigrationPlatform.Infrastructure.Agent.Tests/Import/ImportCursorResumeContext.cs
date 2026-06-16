// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
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
/// Shared scenario state for Import Cursor Resume step definitions.
/// </summary>
public class ImportCursorResumeContext
{
    public const string EndpointUrl = "https://dev.azure.com/contoso";
    public const string ProjectName = "Shop";
    public const string CursorIdentity = "import.workitems";

    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<ICurrentJobEndpointAccessor> MockEndpointAccessor { get; } = new(MockBehavior.Strict);
    public Mock<IPackageAccess> MockPackage { get; }

    public CheckpointingService CheckpointingService { get; }
    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    /// <summary>All folders in the package.</summary>
    public List<string> AllFolderPaths { get; set; } = new();

    /// <summary>Folder paths actually fed to WorkItemResolutionProcessor.</summary>
    public List<string> ProcessedFolders { get; } = new();

    /// <summary>The cursor that was deleted (captured by DeleteCursorAsync).</summary>
    public bool CursorWasDeleted { get; set; }

    /// <summary>Stages that were skipped for the mid-folder resume scenario.</summary>
    public List<string> SkippedStages { get; } = new();

    public ImportCursorResumeContext()
    {
        MockPackage = PackageTestFactory.CreateLooseMock();
        var target = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        target.SetupGet(t => t.Url).Returns(EndpointUrl);
        target.SetupGet(t => t.Project).Returns(ProjectName);
        target.SetupGet(t => t.ConnectorType).Returns("AzureDevOpsServices");

        MockEndpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        MockEndpointAccessor.SetupGet(a => a.Target).Returns(target.Object);

        CheckpointingService = new CheckpointingService(
            currentJobEndpointAccessor: MockEndpointAccessor.Object,
            package: MockPackage.Object);
        MockPackage
            .Setup(p => p.RequestMetaAsync(It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && c.Module == "workitems"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageMetaResult("import.workitems", null));
        MockPackage
            .Setup(p => p.ResetMetaAsync(It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && c.Module == "workitems"), It.IsAny<CancellationToken>()))
            .Callback(() => CursorWasDeleted = true)
            .Returns(ValueTask.CompletedTask);
    }

    public WorkItemRevisionLoopDriver BuildOrchestrator()
    {
        var processor = new WorkItemResolutionProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            CheckpointingService,
            (IIdentityTranslationTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            EndpointUrl,
            ProjectName,
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) },
            package: MockPackage.Object);

        return new WorkItemRevisionLoopDriver(new WorkItemRevisionJobScope(
            MockPackage.Object,
            EndpointUrl,
            ProjectName,
            CheckpointingService,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            JobId: null,
            FilterOptions: null));
    }
}
