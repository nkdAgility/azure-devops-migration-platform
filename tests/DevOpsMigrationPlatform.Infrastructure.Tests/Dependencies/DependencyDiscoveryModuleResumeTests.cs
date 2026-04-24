using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Dependencies;

[TestClass]
public class DependencyDiscoveryModuleResumeTests
{
    private const string CursorKey = ".migration/Checkpoints/dependencies.cursor.json";

    // ── helpers ───────────────────────────────────────────────────────────────

    private static DiscoveryJob MakeJob() => new()
    {
        JobId = "test-job",
        ConfigVersion = "1.0",
        DiscoveryType = DiscoveryJobType.Dependencies,
        Organisations = new List<ScopedOrganisationEndpoint>(),
        Policies = new JobPolicies { CheckpointIntervalSeconds = 60 },
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
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance);

        var ctx = new DiscoveryContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.RunAsync(ctx, CancellationToken.None);

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
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance);

        var ctx = new DiscoveryContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.RunAsync(ctx, CancellationToken.None);

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
                It.IsAny<CancellationToken>()))
            .Returns(EmptyAsync());

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance);

        var ctx = new DiscoveryContext
        {
            Job = MakeJob(),
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.RunAsync(ctx, CancellationToken.None);

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
                It.IsAny<CancellationToken>()))
            .Returns(SingleEventAsync(heartbeat));

        var job = MakeJob();
        // Checkpoint interval of 0 to force an immediate checkpoint write after the heartbeat
        var jobWithZeroInterval = new DiscoveryJob
        {
            JobId = job.JobId,
            ConfigVersion = job.ConfigVersion,
            DiscoveryType = job.DiscoveryType,
            Organisations = job.Organisations,
            Policies = new JobPolicies { CheckpointIntervalSeconds = 0 },
            Package = job.Package
        };

        var mockFactory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.Create(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                It.IsAny<JobPolicies>()))
            .Returns(mockService.Object);

        var sut = new DependencyDiscoveryModule(
            mockFactory.Object,
            NullLogger<DependencyDiscoveryModule>.Instance);

        var ctx = new DiscoveryContext
        {
            Job = jobWithZeroInterval,
            ArtefactStore = mockStore.Object,
            StateStore = mockState.Object,
            ProgressSink = mockSink.Object
        };

        // Act
        await sut.RunAsync(ctx, CancellationToken.None);

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
}
