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
