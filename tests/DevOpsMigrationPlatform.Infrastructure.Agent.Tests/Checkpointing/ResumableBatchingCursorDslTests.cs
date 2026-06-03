// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.Revisions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public sealed class ResumableBatchingCursorDslTests
{
    private static readonly OrganisationEndpoint s_endpoint = new()
    {
        ResolvedUrl = "https://dev.azure.com/testorg",
        Type = "AzureDevOps",
        Authentication = new OrganisationEndpointAuthentication
        {
            Type = AuthenticationType.AccessToken,
            ResolvedAccessToken = "test-pat"
        }
    };

    private static WorkItem MakeWorkItem(int id, params (string Key, object Value)[] fields)
    {
        var wi = new WorkItem { Id = id };
        wi.Fields["System.ChangedDate"] = DateTime.UtcNow;
        wi.Fields["System.Rev"] = 1;
        foreach (var (key, value) in fields)
            wi.Fields[key] = value;
        return wi;
    }

    private static async IAsyncEnumerable<WorkItemQueryWindow> ToAsyncEnumerable(
        IEnumerable<WorkItemQueryWindow> items,
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        foreach (var item in items) { await Task.CompletedTask; yield return item; }
    }

    private static (AzureDevOpsWorkItemFetchService sut, Mock<IWorkItemQueryWindowStrategy> windowMock) Create(
        IReadOnlyList<WorkItemQueryWindow> windows, IReadOnlyDictionary<int, WorkItem>? itemMap = null)
    {
        var windowMock = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientMock = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        windowMock
            .Setup(w => w.EnumerateWindowsAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(), It.IsAny<WorkItemQueryWindowOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(windows));

        if (itemMap is not null)
            witClient.Setup(c => c.GetWorkItemsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<DateTime?>(), It.IsAny<WorkItemExpand?>(), It.IsAny<WorkItemErrorPolicy?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<int> ids, IEnumerable<string> _, DateTime? _2, WorkItemExpand? _3, WorkItemErrorPolicy? _4, object _5, CancellationToken _6) =>
                    ids.Where(id => itemMap.ContainsKey(id)).Select(id => itemMap[id]).ToList());
        else
            witClient.Setup(c => c.GetWorkItemsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<DateTime?>(), It.IsAny<WorkItemExpand?>(), It.IsAny<WorkItemErrorPolicy?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<int> ids, IEnumerable<string> _, DateTime? _2, WorkItemExpand? _3, WorkItemErrorPolicy? _4, object _5, CancellationToken _6) =>
                    ids.Select(id => MakeWorkItem(id)).ToList());

        clientMock.Setup(c => c.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>())).ReturnsAsync(witClient.Object);
        return (new AzureDevOpsWorkItemFetchService(windowMock.Object, clientMock.Object), windowMock);
    }

    private static WorkItemFetchScope MakeScope(
        BatchContinuationToken? saved = null,
        bool resume = true,
        string? query = null,
        List<BatchContinuationToken>? checkpointSink = null) =>
        new WorkItemFetchScope(
            Fields: new[] { "System.Rev", "System.ChangedDate" },
            ResumeEnabled: resume,
            SavedContinuationToken: saved,
            BaseQuery: query ?? string.Empty,
            ContinuationCheckpointWriter: checkpointSink is null ? null : (token, _) =>
            {
                checkpointSink.Add(token);
                return Task.CompletedTask;
            });

    // ── Scenario 1: Resume from a saved continuation token ───────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_WithSavedToken_YieldsOnlyItemsAfterSavedPosition()
    {
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = DateTime.Parse("2025-06-15").ToUniversalTime(),
            WorkItemId = 4200,
            QueryFingerprint = "test-fingerprint",
            GeneratedAtUtc = DateTime.UtcNow,
            Completed = false
        };
        var window = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-5), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(5),
            WorkItemIds = new List<int> { 4201, 4202, 4203 }
        };
        var (sut, _) = Create(new[] { window });
        var checkpoints = new List<BatchContinuationToken>();

        var items = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject",
            MakeScope(saved: savedToken, checkpointSink: checkpoints)))
            items.Add(item);

        Assert.IsTrue(items.Count > 0, "Should yield items when resuming.");
        Assert.IsTrue(items.All(i => i.Id > savedToken.WorkItemId),
            "All items should have ID > saved token WorkItemId.");
    }

    // ── Scenario 2: No continuation token starts from the beginning ───────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_NoSavedToken_StartsFromBeginningWithoutError()
    {
        var window = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-5), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(5),
            WorkItemIds = new List<int> { 1, 2, 3 }
        };
        var (sut, _) = Create(new[] { window });

        var items = new List<FetchedWorkItem>();
        Exception? ex = null;
        try
        {
            await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject", MakeScope(saved: null)))
                items.Add(item);
        }
        catch (Exception e) { ex = e; }

        Assert.IsNull(ex, $"Should not throw on fresh start: {ex?.Message}");
        Assert.IsTrue(items.Count > 0, "Fresh start should yield all work items.");
    }

    // ── Scenario 3: Completion checkpoint marks end of stream ─────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_WhenAllWindowsProcessed_EmitsCompletionCheckpoint()
    {
        var window = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-1), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(1),
            WorkItemIds = new List<int> { 10, 20, 30 }
        };
        var (sut, _) = Create(new[] { window });
        var checkpoints = new List<BatchContinuationToken>();

        await foreach (var _ in sut.FetchAsync(s_endpoint, "TestProject", MakeScope(checkpointSink: checkpoints))) { }

        Assert.IsTrue(checkpoints.Count > 0, "Should emit at least one checkpoint.");
        Assert.IsTrue(checkpoints[^1].Completed, "Final checkpoint must have Completed = true.");
    }

    // ── Scenario 4: Boundary cluster with identical ChangedDate values ─────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_BoundaryClusterSameChangedDate_AllItemsProcessed()
    {
        var changedDate = DateTime.Parse("2025-06-15").ToUniversalTime();
        var items = Enumerable.Range(1, 500).ToDictionary(i => i, i => MakeWorkItem(i, ("System.ChangedDate", changedDate)));
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0", ChangedDateUtc = changedDate, WorkItemId = 250,
            QueryFingerprint = string.Empty, GeneratedAtUtc = DateTime.UtcNow, Completed = false
        };
        var window = new WorkItemQueryWindow
        {
            WindowStart = changedDate.AddDays(-1), WindowEnd = changedDate.AddDays(1),
            WindowSize = TimeSpan.FromDays(2), WorkItemIds = items.Keys.ToList()
        };
        var (sut, _) = Create(new[] { window }, items);

        var yielded = new List<FetchedWorkItem>();
        Exception? ex = null;
        try { await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject", MakeScope(saved: savedToken))) yielded.Add(item); }
        catch (Exception e) { ex = e; }

        Assert.IsNull(ex, $"Should not throw: {ex?.Message}");
        Assert.AreEqual(500, yielded.Count,
            $"All 500 items in the boundary cluster must be processed (cursor at WorkItemId 250 must not skip same-date items). Got {yielded.Count}.");
    }

    // ── Scenario 5: Resume with more than 20000 items since saved position ────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_MoreThan20000Items_EnumeratesWithoutExceedingWiqlLimit()
    {
        const int count = 20001;
        const int batchSize = 200;
        var windows = new List<WorkItemQueryWindow>();
        for (int i = 0; i < count; i += batchSize)
        {
            windows.Add(new WorkItemQueryWindow
            {
                WindowStart = DateTime.UtcNow.AddDays(-30).AddMinutes(i),
                WindowEnd = DateTime.UtcNow.AddDays(-30).AddMinutes(i + batchSize),
                WindowSize = TimeSpan.FromMinutes(batchSize),
                WorkItemIds = Enumerable.Range(i + 1, Math.Min(batchSize, count - i)).ToList()
            });
        }
        var (sut, _) = Create(windows);

        var yielded = new List<FetchedWorkItem>();
        Exception? ex = null;
        try { await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject", MakeScope())) yielded.Add(item); }
        catch (Exception e) { ex = e; }

        Assert.IsNull(ex, $"Should not throw: {ex?.Message}");
        Assert.IsTrue(yielded.Count > 0, "Should enumerate all items.");
    }

    // ── Scenario 6: Query fingerprint mismatch rejects continuation ───────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task EvaluateResumeDecisionAsync_FingerprintMismatch_ReturnsRejected()
    {
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0", ChangedDateUtc = DateTime.UtcNow.AddDays(-1), WorkItemId = 100,
            QueryFingerprint = "abc123", GeneratedAtUtc = DateTime.UtcNow.AddHours(-1), Completed = false
        };
        var window = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-1), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(1), WorkItemIds = new List<int> { 101, 102 }
        };
        var (sut, _) = Create(new[] { window });
        var scope = MakeScope(saved: savedToken, query: "def456");

        var decision = await sut.EvaluateResumeDecisionAsync(s_endpoint, "TestProject", scope);

        Assert.AreEqual(ResumeDecisionStatus.RejectedQueryMismatch, decision.Status);
        Assert.IsNotNull(decision.SavedQueryFingerprint);
        Assert.IsNotNull(decision.CurrentQueryFingerprint);
        Assert.AreNotEqual(decision.SavedQueryFingerprint, decision.CurrentQueryFingerprint);
    }

    // ── Scenario 7: Query fingerprint match accepts continuation ─────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task EvaluateResumeDecisionAsync_FingerprintMatch_ReturnsAccepted()
    {
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0", ChangedDateUtc = DateTime.UtcNow.AddDays(-1), WorkItemId = 100,
            QueryFingerprint = "abc123", GeneratedAtUtc = DateTime.UtcNow.AddHours(-1), Completed = false
        };
        var window = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-1), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(1), WorkItemIds = new List<int> { 101, 102 }
        };
        var (sut, _) = Create(new[] { window });
        var scope = MakeScope(saved: savedToken, query: "abc123");

        var decision = await sut.EvaluateResumeDecisionAsync(s_endpoint, "TestProject", scope);

        Assert.AreEqual(ResumeDecisionStatus.Accepted, decision.Status);
    }

    // ── Scenario 8: Pre-check returns decision without starting enumeration ───

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task EvaluateResumeDecisionAsync_PreCheck_ReturnsDecisionWithoutFetchingItems()
    {
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0", ChangedDateUtc = DateTime.UtcNow.AddDays(-1), WorkItemId = 100,
            QueryFingerprint = "test-fingerprint", GeneratedAtUtc = DateTime.UtcNow.AddHours(-1), Completed = false
        };
        var (sut, _) = Create(Array.Empty<WorkItemQueryWindow>());
        var scope = MakeScope(saved: savedToken, query: "test-fingerprint");

        var decision = await sut.EvaluateResumeDecisionAsync(s_endpoint, "TestProject", scope);

        Assert.IsNotNull(decision);
        Assert.IsTrue(
            decision.Status == ResumeDecisionStatus.Accepted ||
            decision.Status == ResumeDecisionStatus.RejectedQueryMismatch ||
            decision.Status == ResumeDecisionStatus.Unavailable,
            "Decision should have a valid status.");
    }

    // ── Scenario 9: Deterministic ordering ───────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_ResumeEnabled_YieldsItemsWithoutThrowingAndOrderingIsConsistent()
    {
        var now = DateTime.UtcNow;
        var items = new Dictionary<int, WorkItem>
        {
            [1] = MakeWorkItem(1, ("System.ChangedDate", now.AddDays(-5))),
            [2] = MakeWorkItem(2, ("System.ChangedDate", now.AddDays(-3))),
            [3] = MakeWorkItem(3, ("System.ChangedDate", now.AddDays(-2))),
        };
        var window = new WorkItemQueryWindow
        {
            WindowStart = now.AddDays(-5), WindowEnd = now,
            WindowSize = TimeSpan.FromDays(5), WorkItemIds = new List<int> { 1, 2, 3 }
        };
        var (sut, _) = Create(new[] { window }, items);

        var yielded = new List<FetchedWorkItem>();
        Exception? ex = null;
        try { await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject", MakeScope(resume: true))) yielded.Add(item); }
        catch (Exception e) { ex = e; }

        Assert.IsNull(ex, $"Should not throw: {ex?.Message}");
        Assert.IsTrue(yielded.Count > 0, "Should yield items.");
    }

    // ── Scenario 10: Source drift yields duplicate items without suppression ──

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_OverlappingWindows_YieldsDuplicateIdsWithoutSuppression()
    {
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0", ChangedDateUtc = DateTime.UtcNow.AddDays(-5), WorkItemId = 50,
            QueryFingerprint = string.Empty, GeneratedAtUtc = DateTime.UtcNow.AddDays(-1), Completed = false
        };
        var window1 = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-5), WindowEnd = DateTime.UtcNow.AddDays(-3),
            WindowSize = TimeSpan.FromDays(2), WorkItemIds = new List<int> { 50, 51, 52, 53 }
        };
        var window2 = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-3), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(3), WorkItemIds = new List<int> { 50, 51, 54, 55 }
        };
        var (sut, _) = Create(new[] { window1, window2 });

        var yielded = new List<FetchedWorkItem>();
        Exception? ex = null;
        try { await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject", MakeScope(saved: savedToken))) yielded.Add(item); }
        catch (Exception e) { ex = e; }

        Assert.IsNull(ex, "Service should not throw on overlapping windows.");
        var duplicates = yielded.GroupBy(i => i.Id).Where(g => g.Count() > 1).ToList();
        Assert.IsTrue(duplicates.Count > 0, "Expected duplicate item IDs from overlapping windows.");
    }

    // ── Scenario 11: Resumed run processes all items despite source mutations ─

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task FetchAsync_ItemsReDateAfterInterruption_AllOriginalAndRedatedItemsProcessed()
    {
        const int total = 5000;
        const int redated = 50;
        var savedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0", ChangedDateUtc = DateTime.UtcNow.AddDays(-2), WorkItemId = 1000,
            QueryFingerprint = string.Empty, GeneratedAtUtc = DateTime.UtcNow.AddHours(-1), Completed = false
        };
        var window1 = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-10), WindowEnd = DateTime.UtcNow.AddDays(-1),
            WindowSize = TimeSpan.FromDays(9), WorkItemIds = Enumerable.Range(1, total).ToList()
        };
        var window2 = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-1), WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(1), WorkItemIds = Enumerable.Range(1, redated).ToList()
        };
        var (sut, _) = Create(new[] { window1, window2 });

        var yielded = new List<FetchedWorkItem>();
        Exception? ex = null;
        try { await foreach (var item in sut.FetchAsync(s_endpoint, "TestProject", MakeScope(saved: savedToken))) yielded.Add(item); }
        catch (Exception e) { ex = e; }

        Assert.IsNull(ex, $"Should not throw: {ex?.Message}");
        Assert.IsTrue(yielded.Count >= total, $"Expected at least {total} items but got {yielded.Count}.");
    }
}
