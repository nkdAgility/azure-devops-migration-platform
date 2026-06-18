// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems.Extensions;

[TestClass]
public sealed class CommentsWorkItemExtensionTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Contract_DeclaresWorkItemsCommentsImportOnly()
    {
        var ext = new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()));

        Assert.AreEqual("WorkItems", ext.Module);
        Assert.AreEqual("Comments", ext.Name);
        Assert.AreEqual(500, ext.Order);
        Assert.IsTrue(ext.SupportsImport);
        Assert.IsFalse(ext.SupportsExport);
        Assert.IsTrue(ext.IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabled_ReflectsOwnOptions()
    {
        var ext = new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions { Enabled = false }));
        Assert.IsFalse(ext.IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_ReplaysNonDeletedCommentsToResolvedTarget()
    {
        const int targetId = 4242;
        const string folderPath = "WorkItems/2026-01-15/1-42-3";

        const string commentJson =
            "[{\"Text\":\"hello\",\"RenderedText\":\"<p>hello</p>\",\"IsDeleted\":false}," +
            "{\"Text\":\"gone\",\"IsDeleted\":true}," +
            "{\"Text\":\"world\",\"IsDeleted\":false}]";

        var target = new Mock<IWorkItemTarget>(MockBehavior.Strict);
        target
            .Setup(t => t.CreateCommentAsync(targetId, "<p>hello</p>", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        target
            .Setup(t => t.CreateCommentAsync(targetId, "world", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        Func<string, CancellationToken, Task<string?>> readText = (path, _) =>
        {
            Assert.AreEqual($"{folderPath}/comment.json", path);
            return Task.FromResult<string?>(commentJson);
        };

        var ext = new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()));

        await ext.ImportAsync(CreateContext(targetId, folderPath, target.Object, readText), CancellationToken.None);

        target.Verify(t => t.CreateCommentAsync(targetId, "<p>hello</p>", It.IsAny<CancellationToken>()), Times.Once);
        target.Verify(t => t.CreateCommentAsync(targetId, "world", It.IsAny<CancellationToken>()), Times.Once);
        target.Verify(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static WorkItemExtensionContext CreateContext(
        int targetId,
        string folderPath,
        IWorkItemTarget target,
        Func<string, CancellationToken, Task<string?>> readText)
        => new()
        {
            Organisation = "org",
            ProjectName = "proj",
            EntityId = "42",
            TargetEntityId = targetId.ToString(),
            Package = Mock.Of<IPackageAccess>(),
            TargetWorkItemId = targetId,
            FolderPath = folderPath,
            Target = target,
            ReadTextAsync = readText,
            Revision = new WorkItemRevision
            {
                WorkItemId = 42,
                RevisionIndex = 3,
                ChangedDate = DateTimeOffset.UtcNow,
            }
        };
}
