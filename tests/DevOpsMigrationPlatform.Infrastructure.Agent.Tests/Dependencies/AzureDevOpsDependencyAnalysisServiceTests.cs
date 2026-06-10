// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Dependencies;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Tests.Dsl;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Dependencies;

[TestClass]
public class AzureDevOpsDependencyAnalysisServiceTests
{
    private Mock<IOptions<MigrationPlatformOptions>> _optionsMock = null!;
    private Mock<IAzureDevOpsClientFactory> _clientFactoryMock = null!;
    private Mock<IWorkItemFetchService> _fetchServiceMock = null!;
    private Mock<IWorkItemDiscoveryService> _discoveryServiceMock = null!;
    private Mock<ILogger<AzureDevOpsDependencyAnalysisService>> _loggerMock = null!;
    private AzureDevOpsDependencyAnalysisService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _optionsMock = new Mock<IOptions<MigrationPlatformOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(new MigrationPlatformOptions { Policies = new() { Throttle = new() { MaxConcurrency = 4 } } });

        _clientFactoryMock = new Mock<IAzureDevOpsClientFactory>();
        _fetchServiceMock = new Mock<IWorkItemFetchService>();
        _discoveryServiceMock = new Mock<IWorkItemDiscoveryService>();
        _loggerMock = new Mock<ILogger<AzureDevOpsDependencyAnalysisService>>();

        _service = new AzureDevOpsDependencyAnalysisService(
            _optionsMock.Object,
            _clientFactoryMock.Object,
            _fetchServiceMock.Object,
            _discoveryServiceMock.Object,
            _loggerMock.Object);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task AnalyseLinksAsync_WithToken_SetsResumeEnabledTrue()
    {
        // Arrange: supply a continuation token — the service should pass
        // ResumeEnabled=true to IWorkItemFetchService.FetchAsync().
        var endpoint = new AzureDevOpsEndpointOptions
        {
            Url = "https://dev.azure.com/testorg",
            Authentication = new EndpointAuthenticationOptions { AccessToken = "fake" }
        };
        var token = new BatchContinuationToken
        {
            QueryFingerprint = "fp",
            StrategyVersion = "1.0.0",
            WorkItemId = 42,
            ChangedDateUtc = DateTime.UtcNow
        };

        WorkItemFetchScope? capturedScope = null;
        _fetchServiceMock
            .Setup(f => f.FetchAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", It.IsAny<WorkItemFetchScope>(), It.IsAny<CancellationToken>()))
            .Callback<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>((_, _, scope, _) => capturedScope = scope)
            .Returns(AsyncEnumerable.Empty<FetchedWorkItem>());

        _discoveryServiceMock
            .Setup(d => d.CountWorkItemsAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", null, It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ProjectDiscoverySummary>());

        // Act: drain the stream
        await foreach (var _ in _service.AnalyseLinksAsync(endpoint, "TestProject", savedContinuationToken: token)) { }

        // Assert
        Assert.IsNotNull(capturedScope, "FetchAsync should have been called with a scope.");
        Assert.IsTrue(capturedScope!.ResumeEnabled, "ResumeEnabled should be true when a token is supplied.");
        Assert.AreSame(token, capturedScope.SavedContinuationToken, "The exact token should be passed through.");
    }

    // ── dependency-pre-filter: Scenario 1 ────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task AnalyseLinksAsync_WithTypeFilter_OnlyExpandsRelationsForMatchingItems()
    {
        // Arrange: 100 items — Bug (1–40), Task (41–80), Epic (81–100).
        // The Bug-only filter is passed to AnalyseLinksAsync so the service
        // builds a scope with FilterOptions, and the mock FetchAsync respects
        // those FilterOptions, yielding only the 40 Bug items.
        var bugFilter = new WorkItemFetchScopeBuilder().WithTypeFilter("Bug").BuildFilters();
        var harness = new DependencyAnalysisHarness()
            .WithFetchedItems(
                FetchedWorkItemFactory.Range(1, 40, "Bug")
                .Concat(FetchedWorkItemFactory.Range(41, 40, "Task"))
                .Concat(FetchedWorkItemFactory.Range(81, 20, "Epic")));

        // Act: pass the Bug-only filter so the service includes it in the scope.
        await harness.ActAsync(fieldFilters: bugFilter);

        // Assert: Relations-expand was called only for the 40 Bug IDs (1–40).
        var expandedIds = harness.RelationsCapture.AllExpandedIds;
        for (var id = 1; id <= 40; id++)
            Assert.IsTrue(expandedIds.Contains(id), $"Expected Bug ID {id} to be Relations-expanded");
        for (var id = 41; id <= 100; id++)
            Assert.IsFalse(expandedIds.Contains(id), $"Expected non-Bug ID {id} NOT to be Relations-expanded");
    }

    // ── dependency-pre-filter: Scenario 2 ────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task AnalyseLinksAsync_WithTypeFilter_EmitsNoDependencyFoundEventForNonMatchingTypes()
    {
        // Arrange: Bug IDs 1–10 and Task IDs 11–20.
        // The Bug-only filter ensures FetchAsync only yields Bug items,
        // so no DependencyFoundEvent can originate from a Task.
        var bugFilter = new WorkItemFetchScopeBuilder().WithTypeFilter("Bug").BuildFilters();
        var harness = new DependencyAnalysisHarness()
            .WithFetchedItems(
                FetchedWorkItemFactory.Range(1, 10, "Bug")
                .Concat(FetchedWorkItemFactory.Range(11, 10, "Task")));

        // Act: pass the Bug-only filter so the service scopes FetchAsync to Bugs.
        var events = await harness.ActAsync(fieldFilters: bugFilter);

        // Assert: no DependencyFoundEvent may carry a Task source type.
        var foundEvents = events.OfType<DependencyFoundEvent>().ToList();
        foreach (var ev in foundEvents)
            Assert.AreNotEqual("Task", ev.Record.SourceWorkItemType,
                "DependencyFoundEvent must not be emitted for non-matching type 'Task'");
    }

    // ── dependency-pre-filter: Scenario 3 (resume-enabled baseline) ──────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task AnalyseLinksAsync_WithNoToken_SetsResumeEnabledFalse()
    {
        // Arrange: no token — the service should default to ResumeEnabled=false.
        var endpoint = new AzureDevOpsEndpointOptions
        {
            Url = "https://dev.azure.com/testorg",
            Authentication = new EndpointAuthenticationOptions { AccessToken = "fake" }
        };

        WorkItemFetchScope? capturedScope = null;
        _fetchServiceMock
            .Setup(f => f.FetchAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", It.IsAny<WorkItemFetchScope>(), It.IsAny<CancellationToken>()))
            .Callback<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>((_, _, scope, _) => capturedScope = scope)
            .Returns(AsyncEnumerable.Empty<FetchedWorkItem>());

        _discoveryServiceMock
            .Setup(d => d.CountWorkItemsAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", null, It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ProjectDiscoverySummary>());

        // Act
        await foreach (var _ in _service.AnalyseLinksAsync(endpoint, "TestProject")) { }

        // Assert
        Assert.IsNotNull(capturedScope, "FetchAsync should have been called with a scope.");
        Assert.IsFalse(capturedScope!.ResumeEnabled, "ResumeEnabled should be false when no token is supplied.");
        Assert.IsNull(capturedScope.SavedContinuationToken, "No token should be present in scope.");
    }
}
