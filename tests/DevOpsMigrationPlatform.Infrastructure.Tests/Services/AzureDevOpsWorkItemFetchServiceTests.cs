using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;

[TestClass]
public class AzureDevOpsWorkItemFetchServiceTests
{
    private readonly OrganisationEndpoint _endpoint = new()
    {
        ResolvedUrl = "https://dev.azure.com/testorg",
        Type = "AzureDevOpsServices",
        Authentication = new OrganisationEndpointAuthentication()
    };

    [TestMethod]
    public async Task FetchAsync_EmptyFields_ThrowsArgumentException()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: Array.Empty<string>());

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in sut.FetchAsync(_endpoint, "MyProject", scope))
            {
            }
        });
    }

    [TestMethod]
    public async Task FetchAsync_NullFields_ThrowsArgumentException()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: null!);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in sut.FetchAsync(_endpoint, "MyProject", scope))
            {
            }
        });
    }

    [TestMethod]
    public async Task FetchAsync_EmptyWindow_ReturnsEmptySequence()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                "MyProject",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEmpty<WorkItemQueryWindow>());

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.Rev" });

        var results = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(_endpoint, "MyProject", scope))
            results.Add(item);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task FetchAsync_TwoWindows_StreamsItemsPerBatch()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        var window1 = new WorkItemQueryWindow { WorkItemIds = new[] { 1, 2 } };
        var window2 = new WorkItemQueryWindow { WorkItemIds = new[] { 3 } };

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                "MyProject",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(window1, window2));

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetWorkItemsAsync(
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<WorkItemExpand?>(),
                It.IsAny<WorkItemErrorPolicy?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkItem>
            {
                MakeWorkItem(1, ("System.Rev", 5)),
                MakeWorkItem(2, ("System.Rev", 3))
            });

        witClient
            .Setup(c => c.GetWorkItemsAsync(
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 3 })),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<WorkItemExpand?>(),
                It.IsAny<WorkItemErrorPolicy?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkItem>
            {
                MakeWorkItem(3, ("System.Rev", 1))
            });

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.Rev" });

        var results = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(_endpoint, "MyProject", scope))
            results.Add(item);

        Assert.AreEqual(3, results.Count);
        Assert.AreEqual(1, results[0].Id);
        Assert.AreEqual(2, results[1].Id);
        Assert.AreEqual(3, results[2].Id);
    }

    [TestMethod]
    public async Task FetchAsync_FieldProjection_PassedToGetWorkItemsAsync()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        var window = new WorkItemQueryWindow { WorkItemIds = new[] { 1 } };

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                "MyProject",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(window));

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        string[]? capturedFields = null;
        witClient
            .Setup(c => c.GetWorkItemsAsync(
                It.IsAny<IEnumerable<int>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<WorkItemExpand?>(),
                It.IsAny<WorkItemErrorPolicy?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<int>, IEnumerable<string>, DateTime?, WorkItemExpand?, WorkItemErrorPolicy?, object, CancellationToken>(
                (_, fields, _, _, _, _, _) => capturedFields = fields?.ToArray())
            .ReturnsAsync(new List<WorkItem> { MakeWorkItem(1, ("System.State", "Active")) });

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.State", "System.WorkItemType" });

        await foreach (var _ in sut.FetchAsync(_endpoint, "MyProject", scope)) { }

        Assert.IsNotNull(capturedFields);
        CollectionAssert.AreEquivalent(new[] { "System.State", "System.WorkItemType" }, capturedFields);
    }

    [TestMethod]
    public async Task FetchAsync_CancellationToken_Propagated()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                "MyProject",
                null,
                It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
            .Returns(AsyncEmpty<WorkItemQueryWindow>());

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.Rev" });

        var results = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(_endpoint, "MyProject", scope, cts.Token))
            results.Add(item);

        windowStrategy.Verify(w => w.EnumerateWindowsAsync(
            It.IsAny<OrganisationEndpoint>(),
            "MyProject",
            null,
            It.Is<CancellationToken>(ct => ct.IsCancellationRequested)),
            Times.Once);
    }

    [TestMethod]
    public async Task FetchAsync_MissingFieldOnWorkItem_OmittedFromResult()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        var window = new WorkItemQueryWindow { WorkItemIds = new[] { 1 } };

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(window));

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        // Work item only has System.State, not System.WorkItemType
        witClient
            .Setup(c => c.GetWorkItemsAsync(
                It.IsAny<IEnumerable<int>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<WorkItemExpand?>(),
                It.IsAny<WorkItemErrorPolicy?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkItem> { MakeWorkItem(1, ("System.State", "Active")) });

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.State", "System.WorkItemType" });

        var results = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(_endpoint, "MyProject", scope))
            results.Add(item);

        Assert.AreEqual(1, results.Count);
        Assert.IsTrue(results[0].Fields.ContainsKey("System.State"));
        Assert.IsFalse(results[0].Fields.ContainsKey("System.WorkItemType"));
    }

    [TestMethod]
    public async Task FetchAsync_TransientApiException_PropagatesWithoutBuffering()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        var window = new WorkItemQueryWindow { WorkItemIds = new[] { 1 } };

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(window));

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetWorkItemsAsync(
                It.IsAny<IEnumerable<int>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<WorkItemExpand?>(),
                It.IsAny<WorkItemErrorPolicy?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transient API error"));

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.Rev" });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in sut.FetchAsync(_endpoint, "MyProject", scope)) { }
        });
    }

    [TestMethod]
    public async Task FetchAsync_ZeroIdWindow_ReturnsEmptySequenceNoBatchCalls()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        // Window with zero IDs
        var window = new WorkItemQueryWindow { WorkItemIds = Array.Empty<int>() };

        windowStrategy
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(window));

        clientFactory
            .Setup(c => c.CreateWorkItemClientAsync(_endpoint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        var sut = new AzureDevOpsWorkItemFetchService(windowStrategy.Object, clientFactory.Object);
        var scope = new WorkItemFetchScope(Fields: new[] { "System.Rev" });

        var results = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(_endpoint, "MyProject", scope))
            results.Add(item);

        Assert.AreEqual(0, results.Count);

        // Verify GetWorkItemsAsync was never called
        witClient.Verify(c => c.GetWorkItemsAsync(
            It.IsAny<IEnumerable<int>>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<DateTime?>(),
            It.IsAny<WorkItemExpand?>(),
            It.IsAny<WorkItemErrorPolicy?>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WorkItem MakeWorkItem(int id, params (string Key, object Value)[] fields)
    {
        var wi = new WorkItem { Id = id };
        foreach (var (key, value) in fields)
            wi.Fields[key] = value;
        return wi;
    }

    private static async IAsyncEnumerable<T> AsyncEmpty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.CompletedTask;
            yield return item;
        }
    }
}
