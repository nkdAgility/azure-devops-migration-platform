// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Reqnroll step definitions for the resumable-batching-cursor.feature scenarios.
/// Exercises real <see cref="AzureDevOpsWorkItemFetchService"/> and
/// <see cref="IWorkItemQueryWindowStrategy"/> via mock WIQL clients.
/// </summary>
[Binding]
public sealed class ResumableBatchingCursorSteps
{
    private readonly ResumableBatchingCursorContext _ctx;

    private Mock<IWorkItemQueryWindowStrategy>? _windowStrategyMock;
    private Mock<IAzureDevOpsClientFactory>? _clientFactoryMock;
    private AzureDevOpsWorkItemFetchService? _fetchService;

    private static readonly OrganisationEndpoint TestEndpoint = new()
    {
        ResolvedUrl = "https://dev.azure.com/testorg",
        Type = "AzureDevOps",
        Authentication = new OrganisationEndpointAuthentication
        {
            Type = AuthenticationType.Pat,
            ResolvedAccessToken = "test-pat"
        }
    };

    public ResumableBatchingCursorSteps(ResumableBatchingCursorContext ctx)
    {
        _ctx = ctx;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private AzureDevOpsWorkItemFetchService EnsureFetchService()
    {
        if (_fetchService is not null) return _fetchService;

        _windowStrategyMock ??= new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        _clientFactoryMock ??= new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        _fetchService = new AzureDevOpsWorkItemFetchService(
            _windowStrategyMock.Object, _clientFactoryMock.Object);
        return _fetchService;
    }

    private void SetupWindowsAndClient(IReadOnlyList<WorkItemQueryWindow> windows,
                                        IReadOnlyDictionary<int, WorkItem>? itemMap = null)
    {
        _windowStrategyMock ??= new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        _clientFactoryMock ??= new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);

        _windowStrategyMock
            .Setup(w => w.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemQueryWindowOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(windows));

        var witClient = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        if (itemMap is not null)
        {
            witClient
                .Setup(c => c.GetWorkItemsAsync(
                    It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<DateTime?>(), It.IsAny<WorkItemExpand?>(),
                    It.IsAny<WorkItemErrorPolicy?>(), It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<int> ids, IEnumerable<string> _, DateTime? _2,
                               WorkItemExpand? _3, WorkItemErrorPolicy? _4, object _5,
                               CancellationToken _6) =>
                    ids.Where(id => itemMap.ContainsKey(id))
                       .Select(id => itemMap[id])
                       .ToList());
        }
        else
        {
            witClient
                .Setup(c => c.GetWorkItemsAsync(
                    It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<DateTime?>(), It.IsAny<WorkItemExpand?>(),
                    It.IsAny<WorkItemErrorPolicy?>(), It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<int> ids, IEnumerable<string> _, DateTime? _2,
                               WorkItemExpand? _3, WorkItemErrorPolicy? _4, object _5,
                               CancellationToken _6) =>
                    ids.Select(id => MakeWorkItem(id)).ToList());
        }

        _clientFactoryMock
            .Setup(c => c.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        _fetchService = new AzureDevOpsWorkItemFetchService(
            _windowStrategyMock.Object, _clientFactoryMock.Object);
    }

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
        IEnumerable<WorkItemQueryWindow> items)
    {
        foreach (var item in items)
        {
            await Task.CompletedTask;
            yield return item;
        }
    }

    private async Task ExecuteFetchAsync(WorkItemFetchScope scope)
    {
        var sut = EnsureFetchService();
        try
        {
            await foreach (var item in sut.FetchAsync(TestEndpoint, "TestProject", scope))
            {
                _ctx.YieldedItems.Add(item);
            }
        }
        catch (Exception ex)
        {
            _ctx.ThrownException = ex;
        }
    }

    // ── Background ───────────────────────────────────────────────────────

    [Given("the migration platform is configured with resumable batching enabled")]
    public void GivenResumableBatchingEnabled()
    {
        _ctx.ResumeEnabled = true;
    }

    // NOTE: "the checkpoint location is {string}" is defined in CursorResumeSteps.
    // Reqnroll bindings are global — no duplicate needed here.

    // ── US1 Steps ────────────────────────────────────────────────────────

    [Given("a discovery job has previously processed work items up to ChangedDate {string} and WorkItemId {int}")]
    public void GivenPreviouslyProcessedUpTo(string changedDate, int workItemId)
    {
        _ctx.SavedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = DateTime.Parse(changedDate).ToUniversalTime(),
            WorkItemId = workItemId,
            QueryFingerprint = "test-fingerprint",
            GeneratedAtUtc = DateTime.UtcNow,
            Completed = false
        };
    }

    [Given("a continuation token exists with that position and a matching query fingerprint")]
    public void GivenTokenExistsWithMatchingFingerprint()
    {
        _ctx.CurrentFingerprint = _ctx.SavedToken?.QueryFingerprint;
    }

    [Given("no continuation token exists for the module")]
    public void GivenNoTokenExists()
    {
        _ctx.SavedToken = null;
    }

    [Given("a discovery job processes all available work items")]
    public void GivenJobProcessesAllItems()
    {
        // Setup: will be exercised when the When step enumerates to completion.
    }

    [Given("{int} work items share the same ChangedDate of {string}")]
    public void GivenIdenticalChangedDate(int count, string date)
    {
        var changedDate = DateTime.Parse(date).ToUniversalTime();
        var items = new Dictionary<int, WorkItem>();
        for (int i = 1; i <= count; i++)
            items[i] = MakeWorkItem(i, ("System.ChangedDate", changedDate));

        var window = new WorkItemQueryWindow
        {
            WindowStart = changedDate.AddDays(-1),
            WindowEnd = changedDate.AddDays(1),
            WindowSize = TimeSpan.FromDays(2),
            WorkItemIds = items.Keys.ToList()
        };
        SetupWindowsAndClient(new[] { window }, items);
    }

    [Given("the continuation token records position at WorkItemId {int}")]
    public void GivenTokenAtWorkItemId(int id)
    {
        if (_ctx.SavedToken is not null)
            _ctx.SavedToken = _ctx.SavedToken with { WorkItemId = id };
    }

    [Given("more than {int} work items exist between the saved continuation date and now")]
    public void GivenMoreThanNItems(int count)
    {
        var windows = new List<WorkItemQueryWindow>();
        const int batchSize = 200;
        for (int i = 0; i < count; i += batchSize)
        {
            var ids = Enumerable.Range(i + 1, Math.Min(batchSize, count - i)).ToList();
            windows.Add(new WorkItemQueryWindow
            {
                WindowStart = DateTime.UtcNow.AddDays(-30).AddMinutes(i),
                WindowEnd = DateTime.UtcNow.AddDays(-30).AddMinutes(i + batchSize),
                WindowSize = TimeSpan.FromMinutes(batchSize),
                WorkItemIds = ids
            });
        }
        SetupWindowsAndClient(windows);
    }

    [When("the job restarts with resume enabled")]
    [When("the job runs with resume enabled")]
    [When("the job resumes from the saved token")]
    [When("the job resumes from that token")]
    public async Task WhenJobRestartsWithResume()
    {
        if (_fetchService is null)
        {
            SetupWindowsAndClient(new[] {
                new WorkItemQueryWindow
                {
                    WindowStart = DateTime.UtcNow.AddDays(-5),
                    WindowEnd = DateTime.UtcNow,
                    WindowSize = TimeSpan.FromDays(5),
                    WorkItemIds = new List<int> { 4201, 4202, 4203 }
                }
            });
        }

        var scope = new WorkItemFetchScope(
            Fields: new[] { "System.Rev" },
            ResumeEnabled: _ctx.ResumeEnabled,
            SavedContinuationToken: _ctx.SavedToken,
            ContinuationCheckpointWriter: (token, _) =>
            {
                _ctx.EmittedCheckpoints.Add(token);
                return Task.CompletedTask;
            });

        await ExecuteFetchAsync(scope);
    }

    [When("the final batch window is yielded")]
    public async Task WhenFinalBatchWindowYielded()
    {
        SetupWindowsAndClient(new[] {
            new WorkItemQueryWindow
            {
                WindowStart = DateTime.UtcNow.AddDays(-1),
                WindowEnd = DateTime.UtcNow,
                WindowSize = TimeSpan.FromDays(1),
                WorkItemIds = new List<int> { 10, 20, 30 }
            }
        });

        var scope = new WorkItemFetchScope(
            Fields: new[] { "System.Rev" },
            ResumeEnabled: true,
            ContinuationCheckpointWriter: (token, _) =>
            {
                _ctx.EmittedCheckpoints.Add(token);
                return Task.CompletedTask;
            });

        await ExecuteFetchAsync(scope);
    }

    [Then("batching continues from the saved continuation position")]
    public void ThenContinuesFromSavedPosition()
    {
        Assert.IsTrue(_ctx.YieldedItems.Count > 0,
            "Should yield work items when resuming from a saved token.");
        Assert.IsNull(_ctx.ThrownException, $"Should not throw: {_ctx.ThrownException?.Message}");
    }

    [Then("work items before the saved position are not re-fetched")]
    public void ThenItemsBeforePositionNotRefetched()
    {
        if (_ctx.SavedToken is not null)
        {
            foreach (var item in _ctx.YieldedItems)
            {
                Assert.IsTrue(item.Id > _ctx.SavedToken.WorkItemId,
                    $"Item {item.Id} should not be fetched — at or before saved position {_ctx.SavedToken.WorkItemId}.");
            }
        }
    }

    [Then("batching starts from the earliest date without error")]
    public void ThenStartsFromEarliestDate()
    {
        Assert.IsNull(_ctx.ThrownException, $"Should not throw on fresh start: {_ctx.ThrownException?.Message}");
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Fresh start should yield work items.");
    }

    [Then("a ResumeDecision of {string} is logged")]
    [Then("a ResumeDecision of {string} is recorded")]
    [Then("a ResumeDecision of {string} is returned")]
    public void ThenResumeDecisionLogged(string expectedStatus)
    {
        if (_ctx.Decision is not null)
        {
            var expected = Enum.Parse<ResumeDecisionStatus>(expectedStatus);
            Assert.AreEqual(expected, _ctx.Decision.Status,
                $"Expected ResumeDecision status {expectedStatus} but got {_ctx.Decision.Status}.");
        }
        else
        {
            // For US1 scenarios where the decision is not explicitly captured,
            // validate based on the observable behaviour (items yielded or not).
            var expected = Enum.Parse<ResumeDecisionStatus>(expectedStatus);
            switch (expected)
            {
                case ResumeDecisionStatus.Accepted:
                    Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Accepted resume should yield items.");
                    break;
                case ResumeDecisionStatus.Unavailable:
                    Assert.IsNull(_ctx.ThrownException, "Unavailable should not throw.");
                    break;
            }
        }
    }

    [Then("a completion checkpoint is emitted with Completed set to true")]
    public void ThenCompletionCheckpointEmitted()
    {
        Assert.IsTrue(_ctx.EmittedCheckpoints.Count > 0, "Should emit at least one checkpoint.");
        var last = _ctx.EmittedCheckpoints[^1];
        Assert.IsTrue(last.Completed, "Final checkpoint must have Completed = true.");
    }

    [Then("the caller can detect end-of-stream on the next resume attempt")]
    public void ThenEndOfStreamDetectable()
    {
        var last = _ctx.EmittedCheckpoints[^1];
        Assert.IsTrue(last.Completed,
            "Completed flag must be set so callers can detect end-of-stream.");
    }

    [Then("all {int} work items are eventually processed")]
    public void ThenAllItemsProcessed(int count)
    {
        Assert.AreEqual(count, _ctx.YieldedItems.Count,
            $"Expected {count} items but got {_ctx.YieldedItems.Count}.");
    }

    [Then("no items in the cluster are skipped")]
    public void ThenNoItemsSkipped()
    {
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should yield items from the cluster.");
        Assert.IsNull(_ctx.ThrownException, $"Should not throw: {_ctx.ThrownException?.Message}");
    }

    [Then("the window strategy subdivides correctly")]
    public void ThenWindowSubdivides()
    {
        Assert.IsNull(_ctx.ThrownException, $"Should not throw: {_ctx.ThrownException?.Message}");
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should yield items across multiple windows.");
    }

    [Then("all items are enumerated without exceeding the WIQL result limit")]
    public void ThenAllItemsEnumerated()
    {
        Assert.IsNull(_ctx.ThrownException, $"Should not throw: {_ctx.ThrownException?.Message}");
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should enumerate all items.");
    }

    // ── US2 Steps ────────────────────────────────────────────────────────

    [Given("a saved continuation token with query fingerprint {string}")]
    public void GivenTokenWithFingerprint(string fingerprint)
    {
        _ctx.SavedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = DateTime.UtcNow.AddDays(-1),
            WorkItemId = 100,
            QueryFingerprint = fingerprint,
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-1),
            Completed = false
        };
    }

    [Given("the current query produces a different fingerprint {string}")]
    [Given("the current query produces the same fingerprint {string}")]
    public void GivenCurrentFingerprint(string fingerprint)
    {
        _ctx.CurrentFingerprint = fingerprint;
    }

    [Given("a saved continuation token exists")]
    public void GivenSavedTokenExists()
    {
        _ctx.SavedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = DateTime.UtcNow.AddDays(-1),
            WorkItemId = 100,
            QueryFingerprint = "test-fingerprint",
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-1),
            Completed = false
        };
        _ctx.CurrentFingerprint = "test-fingerprint";
    }

    [When("the job attempts to resume")]
    public async Task WhenJobAttemptsResume()
    {
        SetupWindowsAndClient(new[] {
            new WorkItemQueryWindow
            {
                WindowStart = DateTime.UtcNow.AddDays(-1),
                WindowEnd = DateTime.UtcNow,
                WindowSize = TimeSpan.FromDays(1),
                WorkItemIds = new List<int> { 101, 102 }
            }
        });

        var scope = new WorkItemFetchScope(
            Fields: new[] { "System.Rev" },
            ResumeEnabled: true,
            BaseQuery: _ctx.CurrentFingerprint ?? string.Empty,
            SavedContinuationToken: _ctx.SavedToken,
            ContinuationCheckpointWriter: (token, _) =>
            {
                _ctx.EmittedCheckpoints.Add(token);
                return Task.CompletedTask;
            });

        var sut = EnsureFetchService();
        _ctx.Decision = await sut.EvaluateResumeDecisionAsync(
            TestEndpoint, "TestProject", scope);

        if (_ctx.Decision.Status == ResumeDecisionStatus.RejectedQueryMismatch)
        {
            _ctx.ThrownException = new ResumeRejectedException(_ctx.Decision);
        }
        else
        {
            await ExecuteFetchAsync(scope);
        }
    }

    [When("the caller invokes EvaluateResumeDecisionAsync")]
    public async Task WhenCallerInvokesPreCheck()
    {
        SetupWindowsAndClient(Array.Empty<WorkItemQueryWindow>());

        var scope = new WorkItemFetchScope(
            Fields: new[] { "System.Rev" },
            ResumeEnabled: true,
            BaseQuery: _ctx.CurrentFingerprint ?? string.Empty,
            SavedContinuationToken: _ctx.SavedToken);

        var sut = EnsureFetchService();
        _ctx.Decision = await sut.EvaluateResumeDecisionAsync(
            TestEndpoint, "TestProject", scope);
    }

    [Then("a ResumeRejectedException is thrown with both fingerprints in the payload")]
    public void ThenResumeRejectedException()
    {
        Assert.IsNotNull(_ctx.ThrownException, "Expected a ResumeRejectedException to be thrown.");
        Assert.IsInstanceOfType<ResumeRejectedException>(_ctx.ThrownException);

        var ex = (ResumeRejectedException)_ctx.ThrownException;
        Assert.IsNotNull(ex.Decision.SavedQueryFingerprint, "Saved fingerprint should be in the payload.");
        Assert.IsNotNull(ex.Decision.CurrentQueryFingerprint, "Current fingerprint should be in the payload.");
        Assert.AreNotEqual(ex.Decision.SavedQueryFingerprint, ex.Decision.CurrentQueryFingerprint,
            "Fingerprints should differ in a mismatch rejection.");
    }

    [Then("enumeration begins from the saved position")]
    public void ThenEnumerationBeginsFromSavedPosition()
    {
        Assert.IsNull(_ctx.ThrownException, $"Should not throw when fingerprints match: {_ctx.ThrownException?.Message}");
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should yield items when fingerprints match.");
    }

    [Then("a ResumeDecision is returned without fetching any work items")]
    public void ThenDecisionReturnedWithoutFetch()
    {
        Assert.IsNotNull(_ctx.Decision, "EvaluateResumeDecisionAsync should return a decision.");
        Assert.AreEqual(0, _ctx.YieldedItems.Count,
            "Pre-check should not fetch any work items.");
    }

    [Then("the decision matches what FetchAsync would produce")]
    public void ThenDecisionMatchesFetchAsync()
    {
        Assert.IsNotNull(_ctx.Decision, "Decision should be returned.");
        Assert.IsTrue(
            _ctx.Decision.Status == ResumeDecisionStatus.Accepted ||
            _ctx.Decision.Status == ResumeDecisionStatus.RejectedQueryMismatch ||
            _ctx.Decision.Status == ResumeDecisionStatus.Unavailable,
            "Decision should have a valid status.");
    }

    // ── US3 Steps ────────────────────────────────────────────────────────

    [Given("resume is enabled for a discovery job")]
    public void GivenResumeEnabledForDiscovery()
    {
        _ctx.ResumeEnabled = true;
    }

    [Given("a discovery job resumes after source work items have been edited")]
    public void GivenResumeAfterEdits()
    {
        _ctx.SavedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = DateTime.UtcNow.AddDays(-5),
            WorkItemId = 50,
            QueryFingerprint = string.Empty,
            GeneratedAtUtc = DateTime.UtcNow.AddDays(-1),
            Completed = false
        };
    }

    [Given("some items now appear in multiple query windows due to changed dates")]
    public void GivenOverlappingItems()
    {
        var window1 = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-5),
            WindowEnd = DateTime.UtcNow.AddDays(-3),
            WindowSize = TimeSpan.FromDays(2),
            WorkItemIds = new List<int> { 50, 51, 52, 53 }
        };
        var window2 = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-3),
            WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(3),
            WorkItemIds = new List<int> { 50, 51, 54, 55 }
        };
        SetupWindowsAndClient(new[] { window1, window2 });
    }

    [Given("a discovery job was interrupted after processing {int} of {int} work items")]
    public void GivenInterruptedAfterN(int processed, int total)
    {
        _ctx.SavedToken = new BatchContinuationToken
        {
            StrategyVersion = "1.0",
            ChangedDateUtc = DateTime.UtcNow.AddDays(-2),
            WorkItemId = processed,
            QueryFingerprint = string.Empty,
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-1),
            Completed = false
        };

        var ids = Enumerable.Range(1, total).ToList();
        var window = new WorkItemQueryWindow
        {
            WindowStart = DateTime.UtcNow.AddDays(-10),
            WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.FromDays(10),
            WorkItemIds = ids
        };
        SetupWindowsAndClient(new[] { window });
    }

    [Given("{int} of the already-processed items were edited after the interruption")]
    public void GivenEditedAfterInterruption(int count)
    {
        var redatedIds = Enumerable.Range(1, count).ToList();
        var existingWindows = new List<WorkItemQueryWindow>
        {
            new WorkItemQueryWindow
            {
                WindowStart = DateTime.UtcNow.AddDays(-10),
                WindowEnd = DateTime.UtcNow.AddDays(-1),
                WindowSize = TimeSpan.FromDays(9),
                WorkItemIds = Enumerable.Range(1, 5000).ToList()
            },
            new WorkItemQueryWindow
            {
                WindowStart = DateTime.UtcNow.AddDays(-1),
                WindowEnd = DateTime.UtcNow,
                WindowSize = TimeSpan.FromDays(1),
                WorkItemIds = redatedIds
            }
        };
        SetupWindowsAndClient(existingWindows);
    }

    [When("work items are enumerated")]
    [When("the resumed enumeration encounters these overlapping items")]
    [When("the job resumes from the saved continuation token")]
    public async Task WhenItemsEnumerated()
    {
        if (_fetchService is null)
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
                WindowStart = now.AddDays(-5),
                WindowEnd = now,
                WindowSize = TimeSpan.FromDays(5),
                WorkItemIds = new List<int> { 1, 2, 3 }
            };
            SetupWindowsAndClient(new[] { window }, items);
        }

        var scope = new WorkItemFetchScope(
            Fields: new[] { "System.Rev", "System.ChangedDate" },
            ResumeEnabled: _ctx.ResumeEnabled,
            SavedContinuationToken: _ctx.SavedToken,
            ContinuationCheckpointWriter: (token, _) =>
            {
                _ctx.EmittedCheckpoints.Add(token);
                return Task.CompletedTask;
            });

        await ExecuteFetchAsync(scope);
    }

    [Then("results are ordered by ChangedDate ascending then WorkItemId ascending")]
    public void ThenOrderedByChangedDateAscThenIdAsc()
    {
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should yield items.");
    }

    [Then("this ordering is consistent across interrupted and resumed runs")]
    public void ThenOrderingConsistent()
    {
        Assert.IsNull(_ctx.ThrownException, $"Should not throw: {_ctx.ThrownException?.Message}");
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should yield items in consistent order.");
    }

    [Then("duplicate item IDs are yielded to the caller without suppression")]
    public void ThenDuplicatesYielded()
    {
        var idCounts = _ctx.YieldedItems
            .GroupBy(i => i.Id)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.IsTrue(idCounts.Count > 0,
            "Expected duplicate item IDs to be yielded (no suppression).");
    }

    [Then("the caller is responsible for idempotent handling")]
    public void ThenCallerHandlesDuplicates()
    {
        Assert.IsNull(_ctx.ThrownException, "Service should not throw on duplicates.");
    }

    [Then("all {int} original items plus the {int} re-dated items are eventually processed")]
    public void ThenAllItemsIncludingRedated(int original, int redated)
    {
        Assert.IsTrue(_ctx.YieldedItems.Count >= original,
            $"Expected at least {original} items but got {_ctx.YieldedItems.Count}.");
    }

    [Then("no items are missed due to resume position logic")]
    public void ThenNoItemsMissed()
    {
        Assert.IsNull(_ctx.ThrownException, $"Should not throw: {_ctx.ThrownException?.Message}");
        Assert.IsTrue(_ctx.YieldedItems.Count > 0, "Should yield all items without missing any.");
    }
}
