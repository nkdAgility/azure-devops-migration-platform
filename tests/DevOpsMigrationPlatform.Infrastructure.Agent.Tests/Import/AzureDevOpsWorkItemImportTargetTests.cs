// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class AzureDevOpsWorkItemImportTargetTests
{
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

        var target = new AzureDevOpsWorkItemImportTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        Assert.IsTrue(await target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));
        Assert.IsTrue(await target.WorkItemTypeExistsAsync("User Story", CancellationToken.None));
        Assert.IsFalse(await target.WorkItemTypeExistsAsync("Task", CancellationToken.None));

        witClient.Verify(c => c.GetWorkItemTypesAsync("ProjectA", null, It.IsAny<CancellationToken>()), Times.Once);
    }

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

        var target = new AzureDevOpsWorkItemImportTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        var checks = Enumerable.Range(0, 10)
            .Select(_ => target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));

        var results = await Task.WhenAll(checks);

        Assert.IsTrue(results.All(result => result));
        witClient.Verify(c => c.GetWorkItemTypesAsync("ProjectA", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task WorkItemTypeExistsAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Strict,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });
        var target = new AzureDevOpsWorkItemImportTarget(witClient.Object, "ProjectA", "https://dev.azure.com/testorg");

        ((IDisposable)target).Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));
    }
}
