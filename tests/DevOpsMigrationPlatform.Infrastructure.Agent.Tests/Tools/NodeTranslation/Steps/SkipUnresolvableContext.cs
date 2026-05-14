// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

public class SkipUnresolvableContext
{
    private const string FolderPath = "WorkItems/2024-01-01/00000638000000000001-1-0";

    public bool SkipOnUnresolvableArea { get; set; } = false;
    public bool SkipOnUnresolvableIteration { get; set; } = false;

    private string _areaPath = @"SourceProject\Team A";
    private string _iterationPath = @"SourceProject\Sprint 1";

    public Mock<IWorkItemImportTarget> TargetMock { get; } = new(MockBehavior.Loose);
    public Mock<IIdMapStore> IdMapStoreMock { get; } = new(MockBehavior.Loose);
    public Mock<ICheckpointingService> CheckpointingMock { get; } = new(MockBehavior.Loose);
    public Mock<IIdentityLookupTool> IdentityMappingMock { get; } = new(MockBehavior.Loose);
    public Mock<IArtefactStore> ArtefactStoreMock { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> PackageMock { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemResolutionStrategy> ResolutionStrategyMock { get; } = new(MockBehavior.Loose);

    public bool UpdateFieldsWasCalled { get; private set; }
    public Exception? CaughtException { get; private set; }

    public SkipUnresolvableContext()
    {
        IdentityMappingMock.Setup(s => s.Resolve(It.IsAny<string>())).Returns<string>(id => id);
        IdMapStoreMock.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(99);
        IdMapStoreMock.Setup(s => s.RecordSkippedRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        TargetMock.Setup(t => t.WorkItemExistsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        TargetMock.Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Callback(() => UpdateFieldsWasCalled = true)
            .Returns(Task.CompletedTask);
        TargetMock.Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(),
            It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        CheckpointingMock.Setup(s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ResolutionStrategyMock.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ResolutionStrategyMock.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void SetPaths(string areaPath, string iterationPath)
    {
        _areaPath = areaPath;
        _iterationPath = iterationPath;
    }

    private void SetupRevisionJson()
    {
        var fields = new[]
        {
            new { ReferenceName = "System.WorkItemType", Value = (object)"Task" },
            new { ReferenceName = "System.AreaPath", Value = (object)_areaPath },
            new { ReferenceName = "System.IterationPath", Value = (object)_iterationPath }
        };
        var revision = new
        {
            WorkItemId = 1,
            RevisionIndex = 0,
            Fields = fields,
            Attachments = Array.Empty<object>(),
            RelatedLinks = Array.Empty<object>(),
            ExternalLinks = Array.Empty<object>(),
            Hyperlinks = Array.Empty<object>(),
            EmbeddedImages = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(revision);
        ArtefactStoreMock.Setup(s => s.ReadAsync($"{FolderPath}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        ArtefactStoreMock.Setup(s => s.ReadAsync($"{FolderPath}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        PackageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == $"{FolderPath}/revision.json"),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));
            });
        PackageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath == $"{FolderPath}/comment.json"),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));
    }

    public async Task RunProcessorAsync()
    {
        SetupRevisionJson();

        var opts = new NodeTranslationOptions
        {
            Enabled = true,
            SkipOnUnresolvableArea = SkipOnUnresolvableArea,
            SkipOnUnresolvableIteration = SkipOnUnresolvableIteration,
            AreaPathMappings = [],
            IterationPathMappings = []
        };

        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);
        var context = new ProjectMapping("SourceProject", "TargetProject");

        var processor = new RevisionFolderProcessor(
            TargetMock.Object,
            IdMapStoreMock.Object,
            CheckpointingMock.Object,
            IdentityMappingMock.Object,
            ArtefactStoreMock.Object,
            NullLogger<RevisionFolderProcessor>.Instance,
            nodeStructureTool: tool,
            nodeStructureContext: context,
            nodeStructureOptions: opts,
            package: PackageMock.Object);

        var ext = new WorkItemsModuleExtensions
        {
            LinksEnabled = false,
            AttachmentsEnabled = false,
            Comments = new() { Enabled = false }
        };

        try
        {
            await processor.ProcessAsync(FolderPath, ext, null, ResolutionStrategyMock.Object, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            CaughtException = ex;
        }
    }
}
