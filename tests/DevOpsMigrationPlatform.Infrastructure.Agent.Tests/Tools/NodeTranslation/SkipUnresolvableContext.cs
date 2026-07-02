// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

public class SkipUnresolvableContext
{
    private const string FolderPath = "WorkItems/2024-01-01/00000638000000000001-1-0";
    private const string Organisation = "https://dev.azure.com/fabrikam";
    private const string Project = "SourceProject";

    public bool SkipOnUnresolvableArea { get; set; }
    public bool SkipOnUnresolvableIteration { get; set; }

    private string _areaPath = @"SourceProject\Team A";
    private string _iterationPath = @"SourceProject\Sprint 1";

    public Mock<IWorkItemTarget> TargetMock { get; } = new(MockBehavior.Loose);
    public Mock<IIdMapStore> IdMapStoreMock { get; } = new(MockBehavior.Loose);
    public Mock<ICheckpointingService> CheckpointingMock { get; } = new(MockBehavior.Loose);
    public Mock<IIdentityTranslationTool> IdentityMappingMock { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> PackageMock { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemResolutionStrategy> ResolutionStrategyMock { get; } = new(MockBehavior.Loose);

    public bool UpdateFieldsWasCalled { get; private set; }
    public Exception? CaughtException { get; private set; }

    public SkipUnresolvableContext()
    {
        IdentityMappingMock.Setup(s => s.Translate(It.IsAny<string>())).Returns<string>(id => id);
        IdMapStoreMock.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(99);
        IdMapStoreMock.Setup(s => s.RecordSkippedRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        TargetMock.Setup(t => t.WorkItemExistsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        TargetMock.Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(),
                It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(),
                It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => UpdateFieldsWasCalled = true)
            .Returns(Task.CompletedTask);
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

    public async Task RunProcessorAsync()
    {
        var fields = new[]
        {
            new { ReferenceName = "System.WorkItemType", Value = (object)"Task" },
            new { ReferenceName = "System.AreaPath", Value = (object)_areaPath },
            new { ReferenceName = "System.IterationPath", Value = (object)_iterationPath }
        };
        var revision = new
        {
            WorkItemId = 1, RevisionIndex = 0, Fields = fields,
            Attachments = Array.Empty<object>(), RelatedLinks = Array.Empty<object>(),
            ExternalLinks = Array.Empty<object>(), Hyperlinks = Array.Empty<object>(),
            EmbeddedImages = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(revision);
        SetupPackageContent($"{FolderPath}/revision.json", json);
        SetupPackageContent($"{FolderPath}/comment.json", null);

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

        var processor = new WorkItemResolutionProcessor(
            TargetMock.Object, IdMapStoreMock.Object, CheckpointingMock.Object,
            IdentityMappingMock.Object, NullLogger<WorkItemResolutionProcessor>.Instance,
            Organisation, Project,
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All) },
            nodeStructureTool: tool, nodeStructureContext: context, nodeStructureOptions: opts,
            package: PackageMock.Object);

        try
        {
            await processor.ImportRevisionAsync(FolderPath, null, ResolutionStrategyMock.Object, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            CaughtException = ex;
        }
    }

    private void SetupPackageContent(string relativePath, string? json)
    {
        PackageMock.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith(
                    relativePath.Replace("WorkItems/", string.Empty), StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(json));
    }

    private static ValueTask<PackagePayload?> ToPayload(string? json)
    {
        if (json is null)
            return ValueTask.FromResult<PackagePayload?>(null);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));
    }
}
