// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Dependencies;

[TestClass]
public class DependencyDiscoveryModuleResumeTests
{
    private const string CursorKey = ".migration/Checkpoints/dependencydiscovery.cursor.json";

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Job MakeJob() => new()
    {
        JobId = "test-job",
        ConfigVersion = "2.0",
        Kind = JobKind.Dependencies,
        Package = new JobPackage { PackageUri = "file:///tmp/pkg" }
    };

    private static string BuildCursorJson(
        IEnumerable<string> completedProjects,
        Dictionary<string, object>? projectStats = null)
    {
        var obj = new Dictionary<string, object>
        {
            ["recordCount"] = 0,
            ["completedProjects"] = completedProjects,
            ["savedAt"] = DateTime.UtcNow
        };
        if (projectStats is not null)
            obj["projectStats"] = projectStats;
        return JsonSerializer.Serialize(obj);
    }

    private static async IAsyncEnumerable<DependencyProgressEvent> EmptyAsync(
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunAsync_WhenCursorHasCompletedProjectsWithStats_EmitsSyntheticProjectCompleteEvents()
    {
        // Arrange
        var projectKey = "https://dev.azure.com/myorg|MyProject";

        var stats = new Dictionary<string, object>
        {
            [projectKey] = new
            {
                workItemsAnalysed = 500,
                externalLinksFound = 47,
                crossProjectCount = 47,
                crossOrgCount = 3,
                totalWorkItems = 500
            }
        };
        var cursorJson = BuildCursorJson(new[] { projectKey }, stats);

        var mockState = new Mock<IStateStore>(MockBehavior.Loose);
        mockState.Setup(s => s.ReadAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(cursorJson);
        mockState.Setup(s => s.ReadAsync(It.Is<string>(k => k != CursorKey), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockState.Setup(s => s.DeleteAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.ReadAsync("inventory.json", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        // Return an existing CSV (header only) so the resume reload branch is exercised.
        mockStore.Setup(s => s.ReadAsync("dependencies.csv", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
                               "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation," +
                               "TargetStatus,LinkChangedDate,SourceWorkItemStateCategory\r\n");
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var emittedEvents = new List<ProgressEvent>();
        var mockSink = new Mock<IProgressSink>(MockBehavior.Loose);
        mockSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
                .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        var mockService = new Mock<IDependencyDiscoveryService>(MockBehavior.Loose);
        mockService.Setup(s => s.DiscoverDependenciesAsync(
                It.IsAny<HashSet<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BatchContinuationToken?>(),
                It.IsAny<Func<BatchContinuationToken, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance,
            new DependencyOrchestrator(NullLogger<DependencyOrchestrator>.Instance));

        var ctx = new ExportContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.ExportAsync(ctx, CancellationToken.None);

        // Assert — a synthetic ProjectComplete event must be emitted for the resumed project.
        // The module puts the structured key (orgUrl|project) in Message and counts in Metrics.
        var resumedEvent = emittedEvents.Find(e =>
            e.Stage == "ProjectComplete" &&
            e.Message != null &&
            e.Message.Contains("|"));

        Assert.IsNotNull(resumedEvent, "Expected a synthetic ProjectComplete event for the resumed project.");
        Assert.AreEqual("https://dev.azure.com/myorg|MyProject", resumedEvent.Message);
        Assert.IsNotNull(resumedEvent.Metrics?.Discovery?.Dependencies, "Expected Metrics with dependency counters.");
        Assert.AreEqual(500, resumedEvent.Metrics!.Discovery!.Dependencies!.WorkItemsAnalysed);
    }

    [TestMethod]
    public async Task RunAsync_WhenCursorHasCompletedProjectsWithoutStats_EmitsSyntheticProjectCompleteEventsWithZeroCounts()
    {
        // Arrange — cursor from a previous version that did not write projectStats
        var projectKey = "https://dev.azure.com/myorg|OldProject";
        var cursorJson = BuildCursorJson(new[] { projectKey }, projectStats: null);

        var mockState = new Mock<IStateStore>(MockBehavior.Loose);
        mockState.Setup(s => s.ReadAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(cursorJson);
        mockState.Setup(s => s.ReadAsync(It.Is<string>(k => k != CursorKey), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockState.Setup(s => s.DeleteAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.ReadAsync("inventory.json", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockStore.Setup(s => s.ReadAsync("dependencies.csv", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("SourceWorkItemId,SourceWorkItemType\r\n");
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var emittedEvents = new List<ProgressEvent>();
        var mockSink = new Mock<IProgressSink>(MockBehavior.Loose);
        mockSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
                .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        var mockService = new Mock<IDependencyDiscoveryService>(MockBehavior.Loose);
        mockService.Setup(s => s.DiscoverDependenciesAsync(
                It.IsAny<HashSet<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BatchContinuationToken?>(),
                It.IsAny<Func<BatchContinuationToken, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance,
            new DependencyOrchestrator(NullLogger<DependencyOrchestrator>.Instance));

        var ctx = new ExportContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.ExportAsync(ctx, CancellationToken.None);

        // Assert — event is still emitted with the structured key even for old cursors without stats
        var resumedEvent = emittedEvents.Find(e =>
            e.Stage == "ProjectComplete" &&
            e.Message != null &&
            e.Message.Contains("|"));

        Assert.IsNotNull(resumedEvent, "Expected a synthetic ProjectComplete event even for old cursors without stats.");
        Assert.AreEqual("https://dev.azure.com/myorg|OldProject", resumedEvent.Message);
    }

    [TestMethod]
    public async Task RunAsync_WhenNoCursorExists_DoesNotEmitSyntheticResumeEvents()
    {
        // Arrange
        var mockState = new Mock<IStateStore>(MockBehavior.Loose);
        mockState.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockState.Setup(s => s.DeleteAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var emittedEvents = new List<ProgressEvent>();
        var mockSink = new Mock<IProgressSink>(MockBehavior.Loose);
        mockSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
                .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        var mockService = new Mock<IDependencyDiscoveryService>(MockBehavior.Loose);
        mockService.Setup(s => s.DiscoverDependenciesAsync(
                It.IsAny<HashSet<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BatchContinuationToken?>(),
                It.IsAny<Func<BatchContinuationToken, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance,
            new DependencyOrchestrator(NullLogger<DependencyOrchestrator>.Instance));

        var ctx = new ExportContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.ExportAsync(ctx, CancellationToken.None);

        // Assert — only the Completed event, no synthetic ProjectComplete events
        var projectCompleteBeforeCompleted = emittedEvents.FindAll(e => e.Stage == "ProjectComplete");
        Assert.AreEqual(0, projectCompleteBeforeCompleted.Count,
            "No synthetic ProjectComplete events should be emitted when there is no cursor.");
    }

    [TestMethod]
    public async Task RunAsync_WhenProjectCompletes_SavesStatsInCursor()
    {
        // Arrange — no prior cursor, one project completes during this run
        var orgUrl = "https://dev.azure.com/myorg";
        var projectName = "MyProject";

        var mockState = new Mock<IStateStore>(MockBehavior.Loose);
        mockState.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockState.Setup(s => s.DeleteAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        string? writtenCursorJson = null;
        mockState.Setup(s => s.WriteAsync(CursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, string, CancellationToken>((_, json, _) => writtenCursorJson = json)
                 .Returns(Task.CompletedTask);

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var mockSink = new Mock<IProgressSink>(MockBehavior.Loose);

        // Service emits one heartbeat event with IsComplete=true so the project is tracked
        var heartbeat = new DependencyHeartbeatEvent(
            OrganisationUrl: orgUrl,
            ProjectName: projectName,
            WorkItemsAnalysed: 200,
            ExternalLinksFound: 10,
            CrossProjectCount: 10,
            CrossOrgCount: 2,
            IsComplete: true,
            TotalWorkItems: 200);

        var mockService = new Mock<IDependencyDiscoveryService>(MockBehavior.Loose);
        mockService.Setup(s => s.DiscoverDependenciesAsync(
                It.IsAny<HashSet<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BatchContinuationToken?>(),
                It.IsAny<Func<BatchContinuationToken, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(SingleEventAsync(heartbeat));

        var job = MakeJob();
        // Checkpoint interval of 0 forces an immediate checkpoint write after the heartbeat.
        // Supply interval=0 via IOptions<DiscoveryOptions> since job.Policies no longer exists.
        var zeroIntervalOpts = Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
        {
            Policies = new MigrationPoliciesOptions { Checkpoints = new MigrationCheckpointsOptions { Interval = 0 } }
        });

        var jobWithZeroInterval = job; // Policies now come from DI, not the job

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance,
            new DependencyOrchestrator(NullLogger<DependencyOrchestrator>.Instance),
            discoveryOptions: zeroIntervalOpts);

        var ctx = new ExportContext
        {
            Job = jobWithZeroInterval,
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.ExportAsync(ctx, CancellationToken.None);

        // Assert — cursor should have been written with projectStats
        Assert.IsNotNull(writtenCursorJson, "Cursor should have been written during the run.");
        using var doc = JsonDocument.Parse(writtenCursorJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("projectStats", out var statsObj),
            "Cursor JSON should contain 'projectStats'.");

        var projectKey = $"{orgUrl.TrimEnd('/').ToLowerInvariant()}|{projectName.Trim().ToLowerInvariant()}";
        Assert.IsTrue(statsObj.TryGetProperty(projectKey, out var projStats),
            $"projectStats should contain an entry for '{projectKey}'.");
        Assert.AreEqual(200, projStats.GetProperty("workItemsAnalysed").GetInt32());
        Assert.AreEqual(10, projStats.GetProperty("externalLinksFound").GetInt32());
        Assert.AreEqual(10, projStats.GetProperty("crossProjectCount").GetInt32());
        Assert.AreEqual(2, projStats.GetProperty("crossOrgCount").GetInt32());
        Assert.AreEqual(200, projStats.GetProperty("totalWorkItems").GetInt32());
    }

    private static async IAsyncEnumerable<DependencyProgressEvent> SingleEventAsync(
        DependencyProgressEvent evt,
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        await Task.CompletedTask;
        yield return evt;
    }

    // ── Batch-level resume tests ──────────────────────────────────────────────

    [TestMethod]
    public void StripCsvRowsForProject_RemovesMatchingRows()
    {
        // Arrange
        var csv =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory\r\n" +
            "1,Bug,ProjectA,https://dev.azure.com/org1,Related,CrossProject,2,ProjectB,https://dev.azure.com/org1,Active,,Active\r\n" +
            "3,Task,ProjectB,https://dev.azure.com/org1,Related,CrossProject,4,ProjectA,https://dev.azure.com/org1,Resolved,,Active\r\n" +
            "5,Bug,ProjectA,https://dev.azure.com/org1,Parent,CrossProject,6,ProjectC,https://dev.azure.com/org2,Active,,Active\r\n";

        // Act — strip all rows for ProjectA in org1
        var result = DependencyDiscoveryModule.StripCsvRowsForProject(
            csv, "https://dev.azure.com/org1", "ProjectA", out var strippedCount);

        // Assert
        Assert.AreEqual(2, strippedCount, "Should strip 2 rows for ProjectA");
        Assert.IsTrue(result.Contains("SourceWorkItemId"), "Header should be preserved");
        Assert.IsTrue(result.Contains("3,Task,ProjectB"), "ProjectB row should remain");
        Assert.IsFalse(result.Contains("1,Bug,ProjectA"), "ProjectA row 1 should be stripped");
        Assert.IsFalse(result.Contains("5,Bug,ProjectA"), "ProjectA row 5 should be stripped");
    }

    [TestMethod]
    public void StripCsvRowsForProject_CaseInsensitive()
    {
        var csv =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl\r\n" +
            "1,Bug,PROJECTA,HTTPS://DEV.AZURE.COM/ORG1,Related,CrossProject,2,ProjB,https://dev.azure.com/org1,Active,,Active\r\n";

        var result = DependencyDiscoveryModule.StripCsvRowsForProject(
            csv, "https://dev.azure.com/org1", "ProjectA", out var strippedCount);

        Assert.AreEqual(1, strippedCount);
        Assert.IsFalse(result.Contains("PROJECTA"));
    }

    [TestMethod]
    public void StripCsvRowsForProject_HandlesQuotedFields()
    {
        var csv =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory\r\n" +
            "1,\"Bug, Critical\",\"Project A\",https://dev.azure.com/org1,Related,CrossProject,2,ProjectB,https://dev.azure.com/org1,Active,,Active\r\n";

        var result = DependencyDiscoveryModule.StripCsvRowsForProject(
            csv, "https://dev.azure.com/org1", "Project A", out var strippedCount);

        Assert.AreEqual(1, strippedCount);
        Assert.IsFalse(result.Contains("Bug, Critical"));
    }

    [TestMethod]
    public async Task RunAsync_WhenCursorHasInProgressProject_PassesResumeParamsToService()
    {
        // Arrange — cursor with an in-progress project containing a continuation token
        var completedKey = "https://dev.azure.com/org1|completedproject";
        var inProgressKey = "https://dev.azure.com/org1|inprogressproject";

        var token = new BatchContinuationToken
        {
            ChangedDateUtc = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            WorkItemId = 5000,
            QueryFingerprint = "test-fingerprint",
            Completed = false
        };

        var cursor = new Dictionary<string, object>
        {
            ["recordCount"] = 3,
            ["completedProjects"] = new[] { completedKey },
            ["projectStats"] = new Dictionary<string, object>
            {
                [completedKey] = new { workItemsAnalysed = 100, externalLinksFound = 5, crossProjectCount = 5, crossOrgCount = 0, totalWorkItems = 100 }
            },
            ["inProgressProject"] = new
            {
                key = inProgressKey,
                continuationToken = token,
                processedWorkItems = 3200,
                linksFound = 10,
                crossProjectCount = 8,
                crossOrgCount = 2
            },
            ["savedAt"] = DateTime.UtcNow
        };
        var cursorJson = JsonSerializer.Serialize(cursor);

        var mockState = new Mock<IStateStore>(MockBehavior.Loose);
        mockState.Setup(s => s.ReadAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(cursorJson);
        mockState.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        mockState.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        // CSV with some rows for the in-progress project (will be stripped)
        var existingCsv =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory\r\n" +
            "1,Bug,CompletedProject,https://dev.azure.com/org1,Related,CrossProject,2,OtherProj,https://dev.azure.com/org1,Active,,Active\r\n" +
            "50,Task,InProgressProject,https://dev.azure.com/org1,Related,CrossProject,51,OtherProj,https://dev.azure.com/org1,Active,,Active\r\n";

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.ReadAsync("inventory.json", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockStore.Setup(s => s.ReadAsync("dependencies.csv", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existingCsv);
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var emittedEvents = new List<ProgressEvent>();
        var mockSink = new Mock<IProgressSink>(MockBehavior.Loose);
        mockSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
                .Callback<ProgressEvent>(e => emittedEvents.Add(e));

        // Capture the resume parameters passed to the service
        string? capturedInProgressKey = null;
        BatchContinuationToken? capturedToken = null;
        Func<BatchContinuationToken, CancellationToken, Task>? capturedWriter = null;

        var mockService = new Mock<IDependencyDiscoveryService>(MockBehavior.Loose);
        mockService.Setup(s => s.DiscoverDependenciesAsync(
                It.IsAny<HashSet<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BatchContinuationToken?>(),
                It.IsAny<Func<BatchContinuationToken, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HashSet<string>?, string?, string?, BatchContinuationToken?, Func<BatchContinuationToken, CancellationToken, Task>?, CancellationToken>(
                (_, _, ipk, ipt, cw, _) =>
                {
                    capturedInProgressKey = ipk;
                    capturedToken = ipt;
                    capturedWriter = cw;
                })
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance,
            new DependencyOrchestrator(NullLogger<DependencyOrchestrator>.Instance));

        var ctx = new ExportContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.ExportAsync(ctx, CancellationToken.None);

        // Assert — the in-progress project key and token must be passed through
        Assert.AreEqual(inProgressKey, capturedInProgressKey,
            "In-progress project key should be passed to DiscoverDependenciesAsync");
        Assert.IsNotNull(capturedToken, "Continuation token should be passed to DiscoverDependenciesAsync");
        Assert.AreEqual(5000, capturedToken!.WorkItemId, "Token work item ID should match");
        Assert.AreEqual("test-fingerprint", capturedToken.QueryFingerprint, "Token fingerprint should match");
        Assert.IsNotNull(capturedWriter, "Checkpoint writer callback should be provided");
    }

    [TestMethod]
    public async Task RunAsync_WhenCursorHasInProgressProject_StripsPartialCsvRowsOnResume()
    {
        // Arrange — cursor with in-progress project
        var inProgressKey = "https://dev.azure.com/org1|myproject";
        var token = new BatchContinuationToken
        {
            ChangedDateUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            WorkItemId = 100,
            QueryFingerprint = "fp",
            Completed = false
        };

        var cursor = new Dictionary<string, object>
        {
            ["recordCount"] = 2,
            ["completedProjects"] = Array.Empty<string>(),
            ["inProgressProject"] = new { key = inProgressKey, continuationToken = token, processedWorkItems = 50 },
            ["savedAt"] = DateTime.UtcNow
        };
        var cursorJson = JsonSerializer.Serialize(cursor);

        var mockState = new Mock<IStateStore>(MockBehavior.Loose);
        mockState.Setup(s => s.ReadAsync(CursorKey, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(cursorJson);
        mockState.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        mockState.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var existingCsv =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemStateCategory\r\n" +
            "10,Bug,MyProject,https://dev.azure.com/org1,Related,CrossProject,20,OtherProject,https://dev.azure.com/org1,Active,,Active\r\n" +
            "30,Task,MyProject,https://dev.azure.com/org1,Related,CrossProject,40,OtherProject,https://dev.azure.com/org1,Active,,Active\r\n";

        // Capture the CSV written to the store
        string? writtenCsv = null;
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.ReadAsync("inventory.json", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((string?)null);
        mockStore.Setup(s => s.ReadAsync("dependencies.csv", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existingCsv);
        mockStore.Setup(s => s.WriteAsync("dependencies.csv", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, string, CancellationToken>((_, csv, _) => writtenCsv = csv)
                 .Returns(Task.CompletedTask);
        mockStore.Setup(s => s.WriteAsync(It.Is<string>(k => k != "dependencies.csv"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var mockSink = new Mock<IProgressSink>(MockBehavior.Loose);

        var mockService = new Mock<IDependencyDiscoveryService>(MockBehavior.Loose);
        mockService.Setup(s => s.DiscoverDependenciesAsync(
                It.IsAny<HashSet<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BatchContinuationToken?>(),
                It.IsAny<Func<BatchContinuationToken, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance,
            new DependencyOrchestrator(NullLogger<DependencyOrchestrator>.Instance));

        var ctx = new ExportContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.ExportAsync(ctx, CancellationToken.None);

        // Assert — the CSV written should not contain the partial MyProject rows
        Assert.IsNotNull(writtenCsv, "CSV should have been written to the store");
        Assert.IsTrue(writtenCsv!.Contains("SourceWorkItemId"), "Header should be present");
        Assert.IsFalse(writtenCsv.Contains("10,Bug,MyProject"), "Partial row 1 should be stripped");
        Assert.IsFalse(writtenCsv.Contains("30,Task,MyProject"), "Partial row 2 should be stripped");
    }
}
