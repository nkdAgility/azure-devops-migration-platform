// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class AzureDevOpsWorkItemTargetTests
{
    // #169 live E2E: TF401320 "Rule Error for field State Change Date … ReadOnly". Replaying history
    // (created/changed dates and by, state change date) needs bypassRules, as the TFS target already
    // uses (WorkItemStoreFlags.BypassRules).
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CreateWorkItemAsync_BypassesRules()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        witClient
            .Setup(c => c.CreateWorkItemAsync(It.IsAny<Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument>(), "ProjectA", "Task",
                It.IsAny<bool?>(), true, It.IsAny<bool?>(), It.IsAny<WorkItemExpand?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItem { Id = 7 });
        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        var result = await target.CreateWorkItemAsync("Task", [new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "System.Title", Value = "t" }], CancellationToken.None);

        Assert.AreEqual(7, result.TargetWorkItemId);
    }

    // #169: one source revision = one save. A new item is created with its fields, links and
    // attachments in one create call; nothing is saved to it a second time.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CreateFromRevisionAsync_UploadsAttachmentsThenCreatesInOneSave()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        witClient
            .Setup(c => c.CreateAttachmentAsync(It.IsAny<Stream>(), "screenshot.png", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentReference { Url = "https://dev.azure.com/testorg/_apis/wit/attachments/1" });
        Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument? sent = null;
        witClient
            .Setup(c => c.CreateWorkItemAsync(It.IsAny<Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument>(), "ProjectA", "Task",
                It.IsAny<bool?>(), true, It.IsAny<bool?>(), It.IsAny<WorkItemExpand?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback((Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument doc, string _, string _, bool? _, bool? _, bool? _, WorkItemExpand? _, object _, CancellationToken _) => sent = doc)
            .ReturnsAsync(new WorkItem { Id = 7 });
        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        var result = await target.CreateFromRevisionAsync(
            "Task",
            [new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "System.Title", Value = "t" }],
            [],
            [],
            [new HyperlinkWorkItemLink { Location = "https://example.com" }],
            [new PendingAttachment("screenshot.png", _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1 })))],
            CancellationToken.None);

        Assert.AreEqual(7, result.TargetWorkItemId);
        CollectionAssert.AreEqual(
            new[] { "/fields/System.Title", "/relations/-", "/relations/-" },
            sent!.Select(o => o.Path).ToArray(),
            "Fields, the hyperlink and the attachment go in the one create.");
        witClient.Verify(c => c.UpdateWorkItemAsync(It.IsAny<Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument>(), It.IsAny<int>(),
            It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<WorkItemExpand?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task UpdateFieldsAsync_BypassesRules()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        witClient
            .Setup(c => c.UpdateWorkItemAsync(It.IsAny<Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument>(), 7,
                It.IsAny<bool?>(), true, It.IsAny<bool?>(), It.IsAny<WorkItemExpand?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItem { Id = 7 });
        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        await target.UpdateFieldsAsync(7, [new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "Microsoft.VSTS.Common.StateChangeDate", Value = "2026-11-16T16:47:37.0000000Z" }], CancellationToken.None);

        witClient.VerifyAll();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task WorkItemTypeExistsAsync_CachesTypes_PerTargetInstance()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        witClient
            .Setup(c => c.GetWorkItemTypesAsync("ProjectA", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkItemType>
            {
                new() { Name = "Bug" },
                new() { Name = "User Story" }
            });

        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        Assert.IsTrue(await target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));
        Assert.IsTrue(await target.WorkItemTypeExistsAsync("User Story", CancellationToken.None));
        Assert.IsFalse(await target.WorkItemTypeExistsAsync("Task", CancellationToken.None));

        witClient.Verify(c => c.GetWorkItemTypesAsync("ProjectA", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ConcurrentCalls_LoadTypeCacheOnce()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        witClient
            .Setup(c => c.GetWorkItemTypesAsync("ProjectA", null, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                return new List<WorkItemType> { new() { Name = "Bug" } };
            });

        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        var checks = Enumerable.Range(0, 10)
            .Select(_ => target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));

        var results = await Task.WhenAll(checks);

        Assert.IsTrue(results.All(result => result));
        witClient.Verify(c => c.GetWorkItemTypesAsync("ProjectA", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task WorkItemTypeExistsAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        ((IDisposable)target).Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposedException()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        var target = new AzureDevOpsWorkItemTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");
        var ct = CancellationToken.None;

        ((IDisposable)target).Dispose();

        var operations = new Func<Task>[]
        {
            async () => await target.CreateWorkItemAsync("Bug", [], ct),
            async () => await target.UpdateFieldsAsync(1, [], ct),
            async () => await target.AddLinksAsync(1, [], [], [], ct),
            async () => await target.UploadAttachmentAsync(1, "file.bin", new MemoryStream(), ct),
            async () => await target.ApplyRevisionAsync(1, [], [], [], [], [], ct),
            async () => await target.UploadEmbeddedImageAsync("image.png", new MemoryStream(), ct),
            async () => await target.CreateCommentAsync(1, "test", ct),
            async () => await target.GetExistingRelationsAsync(1, ct),
            async () => await target.WorkItemExistsAsync(1, ct),
            async () => await target.WorkItemTypeExistsAsync("Bug", ct)
        };

        foreach (var operation in operations)
        {
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(operation);
        }
    }
}
