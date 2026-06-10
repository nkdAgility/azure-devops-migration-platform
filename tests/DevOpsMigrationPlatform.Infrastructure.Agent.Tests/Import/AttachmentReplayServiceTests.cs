// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class AttachmentReplayServiceTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ReplayAsync_UploadsUnmappedAttachment_AndPersistsMapping()
    {
        var target = new Mock<IWorkItemTarget>(MockBehavior.Strict);
        var idMapStore = new Mock<IIdMapStore>(MockBehavior.Strict);
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 3,
            Attachments =
            [
                new AttachmentMetadata
                {
                    OriginalName = "sample.bin",
                    RelativePath = "attachments/sample.bin"
                }
            ]
        };

        idMapStore
            .Setup(s => s.GetAttachmentIdAsync(42, 3, "attachments/sample.bin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        target
            .Setup(t => t.UploadAttachmentAsync(101, "sample.bin", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("simulated://101/sample.bin");
        idMapStore
            .Setup(s => s.SetAttachmentMappingAsync(42, 3, "attachments/sample.bin", "simulated://101/sample.bin", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new AttachmentReplayService(target.Object, idMapStore.Object, NullLogger<AttachmentReplayService>.Instance);
        await sut.ReplayAsync(
            revision,
            "WorkItems/2026-01-01/00000000000000000042-42-3",
            101,
            (_, _) => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])),
            null,
            CancellationToken.None);

        target.VerifyAll();
        idMapStore.VerifyAll();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ReplayAsync_WhenAttachmentBinaryNotInEnumeratedSet_SkipsUpload()
    {
        var target = new Mock<IWorkItemTarget>(MockBehavior.Strict);
        var idMapStore = new Mock<IIdMapStore>(MockBehavior.Strict);
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 3,
            Attachments =
            [
                new AttachmentMetadata
                {
                    OriginalName = "sample.bin",
                    RelativePath = "attachments/sample.bin",
                    Size = 7,
                    ContentType = "application/octet-stream"
                }
            ]
        };

        idMapStore
            .Setup(s => s.GetAttachmentIdAsync(42, 3, "attachments/sample.bin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = new AttachmentReplayService(target.Object, idMapStore.Object, NullLogger<AttachmentReplayService>.Instance);
        await sut.ReplayAsync(
            revision,
            "WorkItems/2026-01-01/00000000000000000042-42-3",
            101,
            (_, _) => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])),
            new HashSet<string>(StringComparer.Ordinal) { "WorkItems/2026-01-01/00000000000000000042-42-3/attachments/other.bin" },
            CancellationToken.None);

        target.Verify(
            t => t.UploadAttachmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idMapStore.Verify(
            s => s.SetAttachmentMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
