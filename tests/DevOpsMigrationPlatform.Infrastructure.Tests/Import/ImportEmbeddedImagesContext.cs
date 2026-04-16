using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared state for Import Embedded Image URL Rewriting step definitions.
/// </summary>
public class ImportEmbeddedImagesContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IIdentityMappingService> MockIdentityMapping { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    public string? RevisionJson { get; set; }
    public string? OriginalUrl { get; set; }
    public string? TargetUrl { get; set; }

    public RevisionFolderProcessor BuildProcessor()
    {
        return new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            MockIdentityMapping.Object,
            MockArtefactStore.Object,
            NullLogger<RevisionFolderProcessor>.Instance);
    }

    public void SetupMocks()
    {
        var folder = "WorkItems/2024-01-01/00000638000000000001-1-0";
        var json = RevisionJson ?? """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

        MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        if (OriginalUrl is not null)
        {
            MockArtefactStore
                .Setup(s => s.ReadBinaryAsync($"{folder}/img1.png", It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 })));
            MockArtefactStore
                .Setup(s => s.ReadBinaryAsync($"{folder}/img2.png", It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 })));

            MockTarget
                .Setup(t => t.UploadEmbeddedImageAsync("img1.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TargetUrl ?? "https://target.example.com/attachments/img1.png");
            MockTarget
                .Setup(t => t.UploadEmbeddedImageAsync("img2.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://target.example.com/attachments/img2.png");
        }

        // Cursor
        MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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

        // Identity pass-through
        MockIdentityMapping
            .Setup(s => s.Resolve(It.IsAny<string>()))
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
