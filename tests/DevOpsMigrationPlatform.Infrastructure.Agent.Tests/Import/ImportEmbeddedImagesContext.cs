// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared state for Import Embedded Image URL Rewriting step definitions.
/// </summary>
public class ImportEmbeddedImagesContext
{
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IIdentityTranslationTool> MockIdentityMapping { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IPackageAccess> MockPackage { get; }

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    public EmbeddedImagesExtensionOptionsConfig? EmbeddedImagesOptions { get; set; }

    public string? RevisionJson { get; set; }
    public string? OriginalUrl { get; set; }
    public string? TargetUrl { get; set; }

    public ImportEmbeddedImagesContext()
    {
        MockPackage = PackageTestFactory.CreateLooseMock();
    }

    public WorkItemResolutionProcessor BuildProcessor()
    {
        return new WorkItemResolutionProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            MockIdentityMapping.Object,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            package: MockPackage.Object,
            embeddedImagesOptions: EmbeddedImagesOptions);
    }

    public void SetupMocks()
    {
        var folder = "WorkItems/2024-01-01/00000638000000000001-1-0";
        var json = RevisionJson ?? """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

        MockPackage
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{folder.Replace("WorkItems/", string.Empty)}/revision.json", System.StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false), "application/json"));
            });
        MockPackage
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{folder.Replace("WorkItems/", string.Empty)}/comment.json", System.StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));

        if (OriginalUrl is not null)
        {
            MockPackage
                .Setup(p => p.RequestContentBinaryAsync(It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{folder.Replace("WorkItems/", string.Empty)}/img1.png", System.StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, writable: false)));
            MockPackage
                .Setup(p => p.RequestContentBinaryAsync(It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{folder.Replace("WorkItems/", string.Empty)}/img2.png", System.StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, writable: false)));

            MockTarget
                .Setup(t => t.UploadEmbeddedImageAsync("img1.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TargetUrl ?? "https://target.example.com/attachments/img1.png");
            MockTarget
                .Setup(t => t.UploadEmbeddedImageAsync("img2.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://target.example.com/attachments/img2.png");
        }

        // Cursor
        MockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        // IdMap
        MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new System.Threading.Tasks.ValueTask());

        // Target
        MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Identity pass-through
        MockIdentityMapping
            .Setup(s => s.IsEnabled)
            .Returns(true);
        MockIdentityMapping
            .Setup(s => s.Translate(It.IsAny<string>()))
            .Returns<string>(id => id);

        // Resolution strategy
        MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
    }
}
