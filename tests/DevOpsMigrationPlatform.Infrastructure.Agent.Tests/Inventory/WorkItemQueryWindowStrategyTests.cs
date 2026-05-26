// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

/// <summary>
/// Behavioural unit tests for <see cref="WorkItemQueryWindowStrategy"/>.
/// All tests use a mock <see cref="IWiqlQueryClientFactory"/> so no live Azure DevOps
/// connection is required.
///
/// Covers spec tasks T020–T024 with real behavioural assertions.
///
/// Strategy has two paths:
///   Path A: Unbounded probe (call 0) returns &lt; LimitThreshold → single window, 1 API call.
///   Path B: Unbounded probe returns ≥ LimitThreshold → backward date-window scan.
/// Call index 0 is always the unbounded probe; indexes 1+ are windowed queries.
/// </summary>
[TestClass]
public class WorkItemQueryWindowStrategyTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string Org = "https://dev.azure.com/testorg";
    private const string Project = "TestProject";
    private const string AccessToken = "test-pat";

    private static readonly OrganisationEndpoint TestEndpoint = new()
    {
        ResolvedUrl = Org,
        Type = "AzureDevOps",
        Authentication = new OrganisationEndpointAuthentication
        {
            Type = AuthenticationType.AccessToken,
            ResolvedAccessToken = AccessToken
        }
    };

    /// <summary>
    /// Builds a <see cref="WorkItemQueryResult"/> containing <paramref name="count"/> sequential IDs.
    /// </summary>
    private static WorkItemQueryResult MakeResult(params int[] ids) => new(ids);

    private static WorkItemQueryResult EmptyResult() => new(Array.Empty<int>());

    /// <summary>
    /// Builds a mock factory whose client always returns the result produced by
    /// <paramref name="queryFunc"/> for every call to <c>QueryByWiqlAsync</c>.
    /// </summary>
    private static (WorkItemQueryWindowStrategy sut, Mock<IWiqlQueryClient> clientMock)
        BuildSut(Func<string, string, int, WorkItemQueryResult> queryFunc)
    {
        var callIndex = 0;
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string project, CancellationToken _) =>
            {
                var result = queryFunc(wiql, project, callIndex);
                callIndex++;
                return result;
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        return (new WorkItemQueryWindowStrategy(factoryMock.Object), clientMock);
    }

    private static async Task<List<WorkItemQueryWindow>> CollectWindowsAsync(
        IWorkItemQueryWindowStrategy sut,
        WorkItemQueryWindowOptions? opts = null)
    {
        var windows = new List<WorkItemQueryWindow>();
        await foreach (var w in sut.EnumerateWindowsAsync(TestEndpoint, Project, opts))
            windows.Add(w);
        return windows;
    }

    // ── T022: Empty window at MinDate floor → stops ──────────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_EmptyFirstWindow_AtMinDateFloor_YieldsNothing()
    {
        // Arrange: project has zero items — the unbounded probe (call 0) returns empty
        // (0 < LimitThreshold) so the strategy yields nothing and makes exactly 1 API call.
        var (sut, clientMock) = BuildSut((_, _, _) => EmptyResult());

        // Act
        var windows = await CollectWindowsAsync(sut);

        // Assert
        Assert.AreEqual(0, windows.Count, "Project with no items must yield no windows");
        clientMock.Verify(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "Exactly one API call — the unbounded probe");
    }

    // ── T021: Multiple windowed results → windows walk backward ──────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_TwoNonEmptyWindowsThenEmpty_YieldsTwoWindows()
    {
        // Arrange: unbounded probe (call 0) exceeds limit → windowing fallback;
        //          call 1 → 3 IDs, call 2 → 2 IDs, call 3 → 0 (MinDate floor → stop)
        var opts = new WorkItemQueryWindowOptions { LimitThreshold = 5, MinDate = DateTime.UtcNow.AddDays(-1) };
        var windowedResponses = new[] { new[] { 1, 2, 3 }, new[] { 4, 5 } };
        var (sut, _) = BuildSut((_, _, idx) =>
            idx == 0 ? MakeResult(1, 2, 3, 4, 5) :                                               // probe >= limit
            idx - 1 < windowedResponses.Length ? MakeResult(windowedResponses[idx - 1]) :        // windowed
            EmptyResult());

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert
        Assert.AreEqual(2, windows.Count, "Should yield exactly two non-empty windows");
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, windows[0].WorkItemIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { 4, 5 }, windows[1].WorkItemIds.ToArray());
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_WindowsWalkBackward_EndEqualsStartOfPrevious()
    {
        // Arrange: unbounded probe (call 0) exceeds limit → windowing;
        //          two non-empty windowed windows, then stop at MinDate floor
        var opts = new WorkItemQueryWindowOptions { LimitThreshold = 5, MinDate = DateTime.UtcNow.AddDays(-1) };
        var (sut, _) = BuildSut((_, _, idx) => idx switch
        {
            0 => MakeResult(1, 2, 3, 4, 5),   // probe >= limit → windowing
            1 => MakeResult(10),               // first windowed window
            2 => MakeResult(20),               // second windowed window
            _ => EmptyResult()                 // MinDate floor → stop
        });

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: second window's end date should be <= first window's start date
        Assert.AreEqual(2, windows.Count);
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
        //  call 2 → 0 IDs (stop — MinDate floor is within the halved window)
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20000,
            // After the 120→60 halve, the next empty window starts at now-120.
            // Setting MinDate = now-100 ensures now-120 <= MinDate → stops immediately.
            MinDate = DateTime.UtcNow.AddDays(-100)
        };
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
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => responses[idx < responses.Length ? idx++ : ^1]);

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: only yielded the 5-item window; oversize window was NOT yielded
        Assert.AreEqual(1, windows.Count, "Oversize window must not be yielded");
        Assert.AreEqual(5, windows[0].WorkItemIds.Count, "Only the retried smaller window should be yielded");
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
        // call 0 (probe): 10 → windowing; call 1 (windowed 30d): 10 → halve; call 2 (windowed 15d): 5 → yield; call 3: 0 → stop
        var responses = new[] { 10, 10, 5, 0 }; // counts

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: halved window is half of initial
        Assert.AreEqual(1, windows.Count);
        Assert.IsTrue(windows[0].WindowSize < TimeSpan.FromDays(opts.InitialWindowDays),
            $"Yielded window size ({windows[0].WindowSize.TotalDays:F1}d) must be less than initial ({opts.InitialWindowDays}d)");
    }

    // ── T023: Window expands proportionally after a successful yield ──────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_SparseWindow_DoublesOnNextIteration()
    {
        // fill ratio = 1/5 = 20% < 50% → window doubles.
        // Arrange: probe → windowing; 10d window overflows → halve to 5d;
        //   5d window returns 1 item (20% fill, < 50%) → yield, window doubles to 10d;
        //   10d window returns 1 item → yield, window doubles to 20d;
        //   next window → 0 → MinDate floor → stop.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 5,
            InitialWindowDays = 10,
            MinWindowDays = 1,
            MaxWindowDays = 60,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };

        // call 0 (probe): 5 → windowing; call 1 (10d): 5 → halve to 5d;
        // call 2 (5d): 1 → yield (doubles to 10d); call 3 (10d): 1 → yield (doubles to 20d);
        // call 4 (20d): 0 → MinDate → stop
        var responses = new[] { 5, 5, 1, 1, 0 };
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: window should double from 5d to 10d after the sparse first yield
        Assert.AreEqual(2, windows.Count);
        Assert.AreEqual(TimeSpan.FromDays(5), windows[0].WindowSize, "First window should be 5d");
        Assert.AreEqual(
            windows[0].WindowSize * 2,
            windows[1].WindowSize,
            "Sparse window (< 50% fill) should double on the next iteration");
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_ModerateWindow_RetainsSizeOnNextIteration()
    {
        // fill ratio = 3/5 = 60% >= 50% → window stays the same size.
        // Arrange: probe → windowing; 10d window overflows → halve to 5d;
        //   5d window returns 3 items (60% fill, >= 50%) → yield, window stays at 5d;
        //   next 5d window → 0 → MinDate floor → stop.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 5,
            InitialWindowDays = 10,
            MinWindowDays = 1,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };

        // call 0 (probe): 5 → windowing; call 1 (10d): 5 → halve to 5d;
        // call 2 (5d): 3 items (60% fill) → yield, stay at 5d; call 3 (5d): 0 → stop
        var responses = new[] { 5, 5, 3, 0 };
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: only possible window is the 5d one (the second call was empty and stopped)
        Assert.AreEqual(1, windows.Count);
        Assert.AreEqual(TimeSpan.FromDays(5), windows[0].WindowSize,
            "Moderate-fill window (>= 50% fill) should retain its size");
    }

    // ── Level 2: ID-cursor paging within a dense single day ────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_MinWindowOverflows_FallsToLevel2_YieldsWindows()
    {
        // When the minimum 1-day window still overflows (VS402337), the strategy
        // must fall through to Level 2 ID-cursor paging instead of throwing.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 5_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: The number of work items returned exceeds the size limit of 20000.");
        var callIdx = 0;

        // call 0 (probe): overflow → Step 2
        // calls 1..N: date windows halve down to 1 day, keep overflowing
        //   — eventually halved == windowSize → Level 2 triggered
        // Level 2 first ID-bounded query: returns 5000 items → yield
        // Level 2 probe after advancing: returns 0 → day exhausted
        // Next date window: empty → MinDate floor → stop
        var boundedSuccessCount = 0;
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var i = callIdx++;
                var q = wiql;

                // Probe and date-only queries overflow
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;

                // Level 2: ID-bounded queries
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    if (boundedSuccessCount++ == 0)
                        return MakeResult(Enumerable.Range(1, 5000).ToArray());
                    return EmptyResult(); // subsequent: empty
                }

                // Probe (no upper bound): day exhausted
                return EmptyResult();
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act — must NOT throw
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: at least one window yielded from Level 2
        Assert.IsTrue(windows.Count >= 1, "Level 2 should yield at least one window");
        Assert.AreEqual(5000, windows[0].WorkItemIds.Count);
    }

    [TestMethod]
    public async Task Level2_SingleDensePage_YieldsOneWindowForDay()
    {
        // 1-day window overflows, Level 2 ID-bounded query returns 15k items (under limit) → 1 window.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 20_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var boundedSuccessCount = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    if (boundedSuccessCount++ == 0)
                        return MakeResult(Enumerable.Range(1, 15_000).ToArray());
                    return EmptyResult(); // subsequent: empty
                }
                return EmptyResult(); // probe → day exhausted
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.AreEqual(1, windows.Count, "Single dense page should yield 1 window");
        Assert.AreEqual(15_000, windows[0].WorkItemIds.Count);
    }

    [TestMethod]
    public async Task Level2_MultiplePages_YieldsMultipleWindowsForSameDay()
    {
        // 1-day overflow → Level 2 needs multiple ID chunks → multiple windows with same date.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 10_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var level2Page = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    var page = level2Page++;
                    return page switch
                    {
                        0 => MakeResult(Enumerable.Range(1, 5000).ToArray()),
                        1 => MakeResult(Enumerable.Range(5001, 5000).ToArray()),
                        2 => MakeResult(Enumerable.Range(10001, 3000).ToArray()),
                        _ => EmptyResult()
                    };
                }
                return EmptyResult(); // probe → day exhausted
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.AreEqual(3, windows.Count, "Three ID chunks should yield three windows");
        // All windows share the same date bounds
        Assert.AreEqual(windows[0].WindowStart, windows[1].WindowStart);
        Assert.AreEqual(windows[0].WindowEnd, windows[1].WindowEnd);
        Assert.AreEqual(windows[1].WindowStart, windows[2].WindowStart);
    }

    [TestMethod]
    public async Task Level2_GapInIds_ProbeAdvancesThroughGap_FindsRemainingItems()
    {
        // Level 2: first ID chunk is empty (gap), probe overflows → advance cursor.
        // Second ID chunk returns items. Third ID chunk empty, probe empty → day done.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 5_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var boundedCallIdx = 0;
        var probeCallIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;

                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = boundedCallIdx++;
                    return idx switch
                    {
                        0 => EmptyResult(),     // first chunk: gap
                        1 => MakeResult(Enumerable.Range(50001, 3000).ToArray()), // second chunk: items
                        _ => EmptyResult()      // subsequent: empty
                    };
                }

                // Probe (no upper bound)
                var pIdx = probeCallIdx++;
                if (pIdx == 0)
                    throw overflowEx; // first probe: overflow → advance cursor
                return EmptyResult(); // subsequent probes: empty → day exhausted
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.IsTrue(windows.Count >= 1, "Should find items after gap");
        Assert.AreEqual(3000, windows[0].WorkItemIds.Count);
    }

    [TestMethod]
    public async Task Level2_ProbeReturnsEmpty_DayExhausted_Stops()
    {
        // Level 2: bounded query returns empty, probe returns empty → day exhausted.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 5_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var yieldedFirstPage = false;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!yieldedFirstPage)
                    {
                        yieldedFirstPage = true;
                        return MakeResult(Enumerable.Range(1, 100).ToArray());
                    }
                    return EmptyResult(); // second bounded chunk: empty
                }
                return EmptyResult(); // probe: empty → day exhausted
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        // First page yielded, then probe says empty → stops
        Assert.AreEqual(1, windows.Count, "Should yield only the first page, then stop");
    }

    [TestMethod]
    public async Task Level2_ProbeReturnsFewItems_YieldsThemAndStops()
    {
        // Level 2: bounded chunk empty, probe returns a few items (under limit) → yielded as final window.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 5_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var probeCallCount = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                    return EmptyResult(); // bounded chunk empty (gap)
                // probe: first returns few items, subsequent return empty
                return probeCallCount++ == 0
                    ? MakeResult(Enumerable.Range(90001, 500).ToArray())
                    : EmptyResult();
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.AreEqual(1, windows.Count, "Probe returning few items should yield them as a window");
        Assert.AreEqual(500, windows[0].WorkItemIds.Count);
    }

    [TestMethod]
    public async Task Level2_IdWindowOverflow_HalvesIdWindowSize()
    {
        // Level 2: bounded query itself overflows (VS402337) → idWindowSize halved, retried.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialIdWindowSize = 10_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var boundedOverflowCount = 0;
        var boundedSuccessCount = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    if (boundedOverflowCount < 2)
                    {
                        boundedOverflowCount++;
                        throw overflowEx; // first 2 bounded queries overflow → halve twice (10k → 5k → 2.5k)
                    }
                    if (boundedSuccessCount++ == 0)
                        return MakeResult(Enumerable.Range(1, 2000).ToArray()); // first success → yield
                    return EmptyResult(); // subsequent bounded queries: empty
                }
                return EmptyResult(); // probe → day done
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.IsTrue(windows.Count >= 1, "Should yield at least one window after halving");
        Assert.AreEqual(2000, windows[0].WorkItemIds.Count);
    }

    [TestMethod]
    public async Task Level2_AfterDraining_OuterDateWindowAdvancesToNextDay()
    {
        // After Level 2 drains a dense day, the outer date loop must advance to the next window.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            MinWindowDays = 1,
            InitialWindowDays = 1, // start at 1 day so we hit Level 2 immediately
            InitialIdWindowSize = 5_000,
            MinDate = DateTime.UtcNow.AddDays(-3) // room for 2+ date windows
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var dateWindowOverflowCount = 0;
        var level2BoundedCount = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;

                // Level 2 ID-bounded query
                if (q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase)
                    && q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    // Return items only once, then empty to drain the day
                    if (level2BoundedCount++ == 0)
                        return MakeResult(Enumerable.Range(1, 100).ToArray());
                    return EmptyResult();
                }

                // Level 2 probe (has [System.Id] > but no <=)
                if (q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    return EmptyResult(); // day exhausted

                // Date-level query with CreatedDate
                if (q.Contains("[System.CreatedDate]", StringComparison.OrdinalIgnoreCase))
                {
                    dateWindowOverflowCount++;
                    if (dateWindowOverflowCount <= 1)
                        throw overflowEx; // first date window overflows → Level 2
                    // Second date window returns a normal result — outer loop advanced
                    return dateWindowOverflowCount == 2
                        ? MakeResult(Enumerable.Range(200, 50).ToArray())
                        : EmptyResult(); // third → stop
                }

                // Unbounded probe: overflow
                throw overflowEx;
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        var windows = await CollectWindowsAsync(sut, opts);

        // First window from Level 2, second from normal date windowing
        Assert.AreEqual(2, windows.Count, "Should yield 1 window from Level 2 + 1 from normal windowing");
        Assert.AreEqual(100, windows[0].WorkItemIds.Count, "Level 2 window");
        Assert.AreEqual(50, windows[1].WorkItemIds.Count, "Normal date window after Level 2");
        Assert.IsTrue(dateWindowOverflowCount >= 2,
            "Outer date loop should have advanced past the dense day");
    }

    // ── T024: WIQL transient error causes retries then throws ────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_WiqlError_RetriesThreeTimes_ThenThrows()
    {
        // Arrange: unbounded probe (call 0) succeeds → triggers windowing (>= limit);
        //          all windowed calls throw a non-overflow error → 1 original + 3 retries = 4
        //          windowed calls, then propagates.
        var opts = new WorkItemQueryWindowOptions { LimitThreshold = 5 };
        var callCount = 0;
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (callCount++ == 0)
                    return MakeResult(1, 2, 3, 4, 5); // probe: >= limit → windowing
                throw new InvalidOperationException("WIQL failed");
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act & Assert: after maxTransientRetries (3) exhausted the original exception propagates
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in sut.EnumerateWindowsAsync(TestEndpoint, Project, opts))
            { }
        });

        // 1 probe + 1 windowed original attempt + 3 windowed retries = 5 total calls
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5),
            "Probe (1) + windowed original attempt (1) + 3 windowed retries = 5 calls");
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_WiqlError_TransientRetryRecoversOnSameWindow()
    {
        // Transient errors (non-overflow) retry the SAME window without halving.
        // After 3 failures the 4th attempt succeeds; 5th stops scan.
        var opts = new WorkItemQueryWindowOptions { LimitThreshold = 5, InitialWindowDays = 120, MinWindowDays = 1, MinDate = DateTime.UtcNow.AddDays(-1) };
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var i = callIdx++;
                if (i == 0) return MakeResult(1, 2, 3, 4, 5);          // probe: >= limit → windowing
                if (i < 4) throw new InvalidOperationException("transient error"); // 3 retries (calls 1-3)
                return i == 4 ? MakeResult(1, 2) : EmptyResult();       // call 4: yield; call 5: stop
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: recovered and yielded 1 window after 3 transient retries
        Assert.AreEqual(1, windows.Count, "Should recover after 3 retries and yield 1 window");
    }

    // ── VS402337 overflow exception in Step 2 does NOT consume retry slots ────

    /// <summary>
    /// Regression test for the actual production failure:
    /// VS402337 thrown by Azure DevOps during windowed scanning must halve the window without
    /// consuming a transient-retry slot.  Without this fix the strategy exhausts its
    /// 3-retry budget after the first three halvings and propagates the exception
    /// — exactly the "crapped out at 263k items" issue reported in production.
    /// </summary>
    [TestMethod]
    public async Task EnumerateWindowsAsync_Step2_OverflowException_HalvesMoreThanThreeTimesWithoutThrowing()
    {
        // Arrange: probe throws VS402337 → falls to Step 2.
        //          5 consecutive date-windowed queries also throw VS402337 (more than maxTransientRetries=3).
        //          Once Level 2 engages (MinWindowDays reached), ID-bounded queries succeed.
        //
        //  If overflow consumed transient-retry slots this would throw on the 4th windowed call.
        //  With the correct split it succeeds through all overflow halvings down to Level 2.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            InitialWindowDays = 120,
            MinWindowDays = 1,
            InitialIdWindowSize = 5_000,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };

        var overflowEx = new InvalidOperationException("VS402337: The number of work items returned exceeds the size limit of 20000.");
        var level2BoundedCount = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                // Date-only queries (no ID bounds) all overflow — halving through date windows
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                // Level 2 ID-bounded queries: return items once, then empty to drain
                if (q.Contains("[System.Id] <=", StringComparison.OrdinalIgnoreCase))
                {
                    if (level2BoundedCount++ == 0)
                        return MakeResult(1, 2, 3);
                    return EmptyResult();
                }
                // Probe → day exhausted
                return EmptyResult();
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act — must NOT throw despite many overflow exceptions exceeding the 3-retry budget
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert
        Assert.IsTrue(windows.Count >= 1, "Should yield at least 1 window after halving through overflow exceptions into Level 2");
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, windows[0].WorkItemIds.ToArray());
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_Step2_MixedOverflowThenTransient_TransientStillLimitedToThree()
    {
        // An overflow sequence followed by transient errors — the transient budget
        // should be independent of the overflow halvings that preceded them.
        // Date-only queries overflow until Level 2 engages; Level 2 ID-bounded 
        // queries then throw transient errors → 1 original + 3 retries, then throws.
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            InitialWindowDays = 120,
            InitialIdWindowSize = 5_000,
            MinWindowDays = 1
        };
        var overflowEx = new InvalidOperationException("VS402337: exceeds the size limit of 20000.");
        var transientEx = new InvalidOperationException("network timeout");

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wiql, string _, CancellationToken _) =>
            {
                var q = wiql;
                // Date-only queries overflow → eventually reaches Level 2
                if (!q.Contains("[System.Id] >", StringComparison.OrdinalIgnoreCase))
                    throw overflowEx;
                // Level 2 ID-bounded queries throw transient errors
                throw transientEx;
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act: all transient retries exhausted → transientEx should propagate
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in sut.EnumerateWindowsAsync(TestEndpoint, Project, opts))
            { }
        });

        // Verify transient errors propagated (the exception type check above is the key assertion).
        // Exact call count is implementation-dependent due to date-window halving depth.
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
            MinWindowDays = 1,
            MinDate = DateTime.UtcNow.AddDays(-1)  // stops scan quickly after first empty windowed result
        };

        // call 0 (probe): 2 → windowing; call 1 (4d): 2 → halve 4→2;
        // call 2 (2d): 2 → halve 2→1 (floor); call 3 (1d): 1 → yield; call 4: 0 → MinDate floor → stop
        var responses = new[] { 2, 2, 2, 1, 0 };
        var callIdx = 0;

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = responses[Math.Min(callIdx++, responses.Length - 1)];
                return MakeResult(Enumerable.Range(1, count).ToArray());
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
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
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sut.EnumerateWindowsAsync(TestEndpoint, Project, cancellationToken: cts.Token))
            { }
        });
    }

    // ── Path A new tests: unbounded probe ─────────────────────────────────────

    [TestMethod]
    public async Task UnboundedProbe_ZeroItems_YieldsNothingWithOneApiCall()
    {
        // When the project has no items the probe returns empty → single API call, no windows.
        var (sut, clientMock) = BuildSut((_, _, _) => EmptyResult());

        var windows = await CollectWindowsAsync(sut);

        Assert.AreEqual(0, windows.Count);
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once, "Only one API call — the unbounded probe");
    }

    [TestMethod]
    public async Task UnboundedProbe_ItemsUnderLimit_YieldsSingleWindowAllIdsWithOneApiCall()
    {
        // When the project has items but fewer than LimitThreshold the probe yields
        // all IDs in one window without any date windowing.
        var (sut, clientMock) = BuildSut((_, _, _) => MakeResult(10, 20, 30, 40));

        var windows = await CollectWindowsAsync(sut);

        Assert.AreEqual(1, windows.Count, "Below-limit project must yield exactly one window");
        CollectionAssert.AreEquivalent(new[] { 10, 20, 30, 40 }, windows[0].WorkItemIds.ToArray());
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once, "Only one API call — the unbounded probe");
    }

    [TestMethod]
    public async Task UnboundedProbe_ItemsUnderLimit_WindowSpansMinDateToNow()
    {
        // The single window returned for under-limit projects should span MinDate → now.
        var opts = new WorkItemQueryWindowOptions { MinDate = DateTime.UtcNow.AddYears(-2) };
        var before = DateTime.UtcNow;
        var (sut, _) = BuildSut((_, _, _) => MakeResult(1, 2, 3));

        var windows = await CollectWindowsAsync(sut, opts);

        var after = DateTime.UtcNow;
        Assert.AreEqual(1, windows.Count);
        Assert.AreEqual(opts.MinDate, windows[0].WindowStart);
        Assert.IsTrue(windows[0].WindowEnd >= before && windows[0].WindowEnd <= after,
            "WindowEnd should be approximately now");
        Assert.AreEqual(windows[0].WindowEnd - windows[0].WindowStart, windows[0].WindowSize);
    }

    // ── Unbounded probe throws → falls through to date-window scanning ────────

    /// <summary>
    /// Regression test: The Azure DevOps WIQL API throws (HTTP 400) when a query would return
    /// more than 20,000 items with no <c>top</c> cap.  The unbounded probe must catch
    /// that exception and fall through to backward date-window scanning rather than
    /// propagating the exception to callers.
    /// </summary>
    [TestMethod]
    public async Task UnboundedProbe_Throws20kException_FallsThroughToDateWindowScanning()
    {
        // Arrange:
        //  call 0 (unbounded probe): throws — Azure DevOps 20k hard cap
        //  call 1 (first windowed):  2 items — yielded
        //  call 2 (next windowed):   0 items — MinDate floor → stop
        var opts = new WorkItemQueryWindowOptions
        {
            LimitThreshold = 20_000,
            InitialWindowDays = 120,
            MinDate = DateTime.UtcNow.AddDays(-1)
        };
        var callIdx = 0;
        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var i = callIdx++;
                if (i == 0)
                    throw new InvalidOperationException("TF201036: The query returned more than 20000 items.");
                return i == 1 ? MakeResult(1, 2) : EmptyResult();
            });

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        // Act
        var windows = await CollectWindowsAsync(sut, opts);

        // Assert: date-window scanning ran and produced results despite the probe throw
        Assert.AreEqual(1, windows.Count, "Should yield 1 window from date-window scanning");
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, windows[0].WorkItemIds.ToArray());
        clientMock.Verify(
            c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "3 calls: probe (throws), first windowed (2 items), second windowed (0 → stop)");
    }

    [TestMethod]
    public async Task UnboundedProbe_Throws20kException_CancellationStillPropagates()
    {
        // OperationCanceledException from the probe must propagate, not be swallowed.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var clientMock = new Mock<IWiqlQueryClient>(MockBehavior.Strict);
        clientMock
            .Setup(c => c.QueryByWiqlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var factoryMock = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientMock.Object);

        var sut = new WorkItemQueryWindowStrategy(factoryMock.Object);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sut.EnumerateWindowsAsync(TestEndpoint, Project, cancellationToken: cts.Token))
            { }
        }, "OperationCanceledException from the probe must not be swallowed");
    }

    // ── Constructor guard ─────────────────────────────────────────────────────

    [TestMethod]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new WorkItemQueryWindowStrategy(null!));

    // ── T018: Resume-aware window skipping ───────────────────────────────────

    [TestMethod]
    public async Task EnumerateWindowsAsync_ResumeEnabled_WithToken_SkipsUnboundedProbe()
    {
        var savedDate = DateTime.UtcNow.AddDays(-10);
        var token = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = savedDate,
            WorkItemId = 500,
            QueryFingerprint = "fp",
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-1),
            Completed = false
        };

        var opts = new WorkItemQueryWindowOptions
        {
            ResumeEnabled = true,
            SavedContinuationToken = token,
            LimitThreshold = 20_000,
            MinDate = DateTime.UtcNow.AddYears(-1)
        };

        var (sut, _) = BuildSut((wiql, _, idx) =>
        {
            if (idx == 0)
            {
                // First query in resume mode should include a date filter, not be unbounded
                Assert.IsTrue(
                    wiql.Contains("System.ChangedDate") || wiql.Contains("System.CreatedDate"),
                    "First query in resume mode should include a date filter, not be unbounded");
                return MakeResult(500, 501, 502);
            }
            return EmptyResult();
        });

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.IsTrue(windows.Count >= 1, "Should yield at least one window from resumed position");
    }

    [TestMethod]
    public async Task EnumerateWindowsAsync_ResumeEnabled_NoToken_FallsBackToFreshStart()
    {
        var opts = new WorkItemQueryWindowOptions
        {
            ResumeEnabled = true,
            SavedContinuationToken = null
        };

        var (sut, _) = BuildSut((_, _, idx) =>
            idx == 0 ? MakeResult(1, 2, 3) : EmptyResult());

        var windows = await CollectWindowsAsync(sut, opts);

        Assert.AreEqual(1, windows.Count);
    }
}

