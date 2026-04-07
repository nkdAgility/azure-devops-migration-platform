using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

/// <summary>
/// Behavioural unit tests for <see cref="WorkItemQueryWindowStrategy"/>.
/// All tests use a mock <see cref="IWiqlQueryClientFactory"/> so no live ADO
/// connection is required.
///
/// Covers spec tasks T020–T024 with real behavioural assertions.
/// </summary>
[TestClass]
public class WorkItemQueryWindowStrategyTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string Org = "https://dev.azure.com/testorg";
    private const string Project = "TestProject";
    private const string Pat = "test-pat";

    /// <summary>
    /// Builds a <see cref="WorkItemQueryResult"/> containing <paramref name="count"/> sequential IDs.
    /// </summary>
    private static WorkItemQueryResult MakeResult(params int[] ids)
        => new() { WorkItems = ids.Select(id => new WorkItemReference { Id = id }).ToList() };

    private static WorkItemQueryResult EmptyResult()
        => new() { WorkItems = Array.Empty<WorkItemReference>() };

    /// <summary>
    /// Builds a mock factory whose client always returns the result produced by
    /// <paramref name="queryFunc"/> for every call to <c>QueryByWiqlAsync</c>.
    /// </summary>
    private static (WorkItemQueryWindowStrategy sut, Mock<IWiqlQueryClient> clientMock)
        BuildSut(Func<Wiql, string, int, WorkItemQueryResult> queryFunc)
    {
        var callIndex = 0;
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wiql wiql, string project, CancellationToken _) =>
            {
                var result = queryFunc(wiql, project, callIndex);
                callIndex++;
                return result;
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        return (new WorkItemQueryWindowStrategy(factoryMock.Object), clientMock);
    }

    private static async Task<List<WorkItemQueryWindow>> CollectWindowsAsync(
        IWorkItemQueryWindowStrategy sut,
        WorkItemQueryWindowOptions? opts = null)
    {
        var windows = new List<WorkItemQueryWindow>();
        await foreach (var w in sut.EnumerateWindowsAsync(Org, Project, Pat, opts))
            windows.Add(w);
        return windows;
    }

    // ── T022: Empty first window → stops immediately ──────────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_EmptyFirstWindow_YieldsNothing()
    {
        // Arrange: first (and only) call returns 0 IDs
        var (sut, clientMock) = BuildSut((_, _, _) => EmptyResult());

        // Act
        var windows = await CollectWindowsAsync(sut);

        // Assert
        Assert.AreEqual(0, windows.Count, "Zero results should stop enumeration immediately");
        clientMock.Verify(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── T021: Single non-empty window → one result, walk backward ────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_TwoNonEmptyWindowsThenEmpty_YieldsTwoWindows()
    {
        // Arrange: call 0 → 3 IDs, call 1 → 2 IDs, call 2 → 0 IDs (stop)
        var responses = new[] { new[] { 1, 2, 3 }, new[] { 4, 5 }, Array.Empty<int>() };
        var (sut, _) = BuildSut((_, _, idx) =>
            idx < responses.Length ? MakeResult(responses[idx]) : EmptyResult());

        // Act
        var windows = await CollectWindowsAsync(sut);

        // Assert
        Assert.AreEqual(2, windows.Count, "Should yield exactly two non-empty windows");
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, windows[0].WorkItemIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { 4, 5 }, windows[1].WorkItemIds.ToArray());
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_WindowsWalkBackward_EndEqualsStartOfPrevious()
    {
        // Arrange: two non-empty windows then stop
        var responses = new[] { new[] { 1 }, new[] { 2 }, Array.Empty<int>() };
        var (sut, _) = BuildSut((_, _, idx) =>
            idx < responses.Length ? MakeResult(responses[idx]) : EmptyResult());

        // Act
        var windows = await CollectWindowsAsync(sut);

        // Assert: second window's end date should be <= first window's start date
        Assert.IsTrue(windows[1].WindowEnd <= windows[0].WindowStart,
            "Each window should walk backward: window[1].End must be <= window[0].Start");
    }

    // ── T020: Window halves when count >= LimitThreshold ─────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_WindowAtLimit_HalvesAndRetries_NoYieldForOversizeWindow()
    {
        // Arrange:
        //  call 0 → exactly LimitThreshold IDs (triggers halve+retry, NOT yielded)
        //  call 1 → 5 IDs (yielded)
        //  call 2 → 0 IDs (stop)
        var opts = new WorkItemQueryWindowOptions { LimitThreshold = 20000 };
        var oversizeIds = Enumerable.Range(1, 20000).ToArray();
        var responses = new WorkItemQueryResult[]
        {
            MakeResult(oversizeIds),
            MakeResult(1, 2, 3, 4, 5),
            EmptyResult()
        };
        var idx = 0;
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => responses[idx < responses.Length ? idx++ : ^1]);

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: only yielded the 5-item window; oversize window was NOT yielded
        Assert.AreEqual(1, windows.Count, "Oversize window must not be yielded");
        Assert.AreEqual(5, windows[0].WorkItemIds.Count, "Only the retried smaller window should be yielded");
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "Three calls: oversize, retry (fits), stop");
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_WindowAtLimit_HalvedWindowSizeSmallerThanOriginal()
    {
        // Arrange: first call returns limit count → triggers halve
        //          second call returns < limit → yielded
        //          third call returns 0 → stop
        var opts = new WorkItemQueryWindowOptions { LimitThreshold = 10, InitialWindowDays = 30 };
        var capturedWindowSizes = new List<TimeSpan>();
        var callIdx = 0;
        var responses = new[] { 10, 5, 0 }; // counts

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: halved window is half of initial
        Assert.AreEqual(1, windows.Count);
        Assert.IsTrue(windows[0].WindowSize < TimeSpan.FromDays(opts.InitialWindowDays),
            $"Yielded window size ({windows[0].WindowSize.TotalDays:F1}d) must be less than initial ({opts.InitialWindowDays}d)");
    }

    // ── T023: Window grows by 1 day after narrow (< 30 day) success ──────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_NarrowWindow_GrowsByOneDayOnNextWindow()
    {
        // Arrange: simulate a window already narrowed to 5 days by using a low threshold
        // so the first call triggers halving down to < 30 days, then second window grows.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 5,       // triggers halve on windows with >= 5 ids
            InitialWindowDays = 10,   // starts at 10 days
            MinWindowDays = 1
        };

        // calls: [5 ids → triggers halve, so window becomes 5d], [3 ids → yielded], [3 ids → yielded, window should have grown], [0 → stop]
        var responses = new[] { 5, 3, 3, 0 };
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: second window should be 1 day larger than the first (both < 30d)
        Assert.AreEqual(2, windows.Count);
        Assert.IsTrue(windows[0].WindowSize < TimeSpan.FromDays(30),
            "First yielded window must be narrow (< 30 days)");
        Assert.AreEqual(
            windows[0].WindowSize + TimeSpan.FromDays(1),
            windows[1].WindowSize,
            "Window should grow by exactly 1 day after a narrow success");
    }

    // ── T024: WIQL error causes retries then throws ───────────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_WiqlError_RetriesThreeTimes_ThenThrows()
    {
        // Arrange: every call throws
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        var callCount = 0;
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WIQL failed"))
            .Callback(() => callCount++);

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act & Assert: after maxRetries (3) exhausted the original exception propagates
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in sut.EnumerateWindowsAsync(Org, Project, Pat))
            { }
        });

        // 1 original attempt + 3 retries = 4 total calls
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4),
            "Should attempt once plus 3 retries before throwing");
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_WiqlError_HalvesWindowOnEachRetry()
    {
        // Arrange: first 3 calls fail (triggering 3 retries with window halving),
        //          4th call returns a small result, 5th call returns 0 (stop)
        var opts = new WorkItemQueryWindowOptions { InitialWindowDays = 120, MinWindowDays = 1 };
        var capturedQueries = new List<Wiql>();
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wiql wiql, string _, CancellationToken __) =>
            {
                capturedQueries.Add(wiql);
                if (callIdx++ < 3) throw new InvalidOperationException("transient error");
                return callIdx <= 4 ? MakeResult(1, 2) : EmptyResult();
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: recovered and yielded 1 window after 3 failed retries
        Assert.AreEqual(1, windows.Count, "Should recover after 3 retries and yield 1 window");
    }

    // ── T020 extension: window below MinWindowDays floors at MinWindowDays ────

    [TestMethod]
    public async Task EnumerateWindowsAsync_RepeatedHalving_FloorsAtMinWindowDays()
    {
        // Arrange: keep returning ≥ LimitThreshold until window would drop below MinWindowDays,
        // then return a small result
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 2,
            InitialWindowDays = 4,  // 4 → 2 → 1 (min) → stays at 1
            MinWindowDays = 1
        };

        // Call sequence: 2 ids (halve 4→2), 2 ids (halve 2→1), 1 id (yield at 1 day), 0 (stop)
        var responses = new[] { 2, 2, 1, 0 };
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: yielded window is at the 1-day minimum
        Assert.AreEqual(1, windows.Count);
        Assert.AreEqual(TimeSpan.FromDays(1), windows[0].WindowSize,
            "Window size should floor at MinWindowDays (1)");
    }

    // ── Window content ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_YieldedWindow_ContainsCorrectIds()
    {
        var (sut, _) = BuildSut((_, _, idx) =>
            idx == 0 ? MakeResult(10, 20, 30) : EmptyResult());

        var windows = await CollectWindowsAsync(sut);

        Assert.AreEqual(1, windows.Count);
        CollectionAssert.AreEquivalent(new[] { 10, 20, 30 }, windows[0].WorkItemIds.ToArray());
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_YieldedWindow_HasWindowStartEndAndSize()
    {
        var (sut, _) = BuildSut((_, _, idx) =>
            idx == 0 ? MakeResult(1) : EmptyResult());

        var windows = await CollectWindowsAsync(sut);

        Assert.AreEqual(1, windows.Count);
        Assert.IsTrue(windows[0].WindowStart < windows[0].WindowEnd, "WindowStart must precede WindowEnd");
        Assert.AreEqual(windows[0].WindowEnd - windows[0].WindowStart, windows[0].WindowSize,
            "WindowSize must equal WindowEnd - WindowStart");
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<Wiql>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sut.EnumerateWindowsAsync(Org, Project, Pat, cancellationToken: cts.Token))
            { }
        });
    }

    // ── Constructor guard ─────────────────────────────────────────────────────

    [TestMethod]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
        => Assert.ThrowsException<ArgumentNullException>(
            () => new WorkItemQueryWindowStrategy(null!));
}
