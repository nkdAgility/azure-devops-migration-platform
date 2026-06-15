// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems.Extensions;

[TestClass]
public sealed class AttachmentsWorkItemExtensionTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Contract_DeclaresWorkItemsAttachmentsImportOnly()
    {
        var ext = new AttachmentsWorkItemExtension(Options.Create(new AttachmentsExtensionOptions()), NullLogger<AttachmentReplayTool>.Instance);

        Assert.AreEqual("WorkItems", ext.Module);
        Assert.AreEqual("Attachments", ext.Name);
        Assert.AreEqual(400, ext.Order);
        Assert.IsTrue(ext.SupportsImport);
        Assert.IsFalse(ext.SupportsExport);
        Assert.IsTrue(ext.IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabled_ReflectsOwnOptions()
    {
        var ext = new AttachmentsWorkItemExtension(Options.Create(new AttachmentsExtensionOptions { Enabled = false }), NullLogger<AttachmentReplayTool>.Instance);
        Assert.IsFalse(ext.IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_ReplaysRevisionAttachmentToResolvedTarget()
    {
        const int targetId = 4242;
        var attachment = new AttachmentMetadata
        {
            OriginalName = "spec.txt",
            RelativePath = "spec.txt",
        };

        var idMap = new Mock<IIdMapStore>(MockBehavior.Loose);
        idMap
            .Setup(m => m.GetAttachmentIdAsync(42, 3, "spec.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var target = new Mock<IWorkItemTarget>(MockBehavior.Strict);
        target
            .Setup(t => t.UploadAttachmentAsync(targetId, "spec.txt", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("target-attachment-id")
            .Verifiable();
        idMap
            .Setup(m => m.SetAttachmentMappingAsync(42, 3, "spec.txt", "target-attachment-id", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var binaryPath = "WorkItems/2026-01-15/1-42-3/spec.txt";
        Func<string, CancellationToken, Task<Stream?>> readBinary =
            (_, _) => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1, 2, 3 }));

        var ext = new AttachmentsWorkItemExtension(Options.Create(new AttachmentsExtensionOptions()), NullLogger<AttachmentReplayTool>.Instance);

        await ext.ImportAsync(
            CreateContext(
                attachment,
                targetId,
                target.Object,
                idMap.Object,
                readBinary,
                new HashSet<string>(StringComparer.Ordinal) { binaryPath }),
            CancellationToken.None);

        target.Verify();
    }

    private static WorkItemExtensionContext CreateContext(
        AttachmentMetadata attachment,
        int targetId,
        IWorkItemTarget? target,
        IIdMapStore? idMapStore,
        Func<string, CancellationToken, Task<Stream?>>? readBinary,
        ISet<string>? availableBinaryPaths)
        => new()
        {
            Organisation = "org",
            ProjectName = "proj",
            EntityId = "42",
            TargetEntityId = targetId.ToString(),
            Package = Mock.Of<IPackageAccess>(),
            TargetWorkItemId = targetId,
            FolderPath = "WorkItems/2026-01-15/1-42-3",
            Target = target,
            IdMapStore = idMapStore,
            ReadBinaryAsync = readBinary,
            AvailableBinaryPaths = availableBinaryPaths,
            Revision = new WorkItemRevision
            {
                WorkItemId = 42,
                RevisionIndex = 3,
                ChangedDate = DateTimeOffset.UtcNow,
                Attachments = new[] { attachment },
            }
        };
}
