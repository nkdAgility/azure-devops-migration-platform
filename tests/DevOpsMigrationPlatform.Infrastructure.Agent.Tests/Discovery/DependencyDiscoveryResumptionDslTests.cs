// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Discovery;

[TestClass]
public sealed class DependencyDiscoveryResumptionDslTests
{
    // Simulated connector always resolves to this URL when no Url is set
    private const string SimulatedOrgUrl = "https://simulated.example.com";

    // ── Scenario: Resume dependency discovery after interruption ────────────────
    // Given a dependency discovery that was interrupted after analysing "ProjectA"
    // When I run dependency discovery again (with ProjectA in completedProjectKeys)
    // Then discovery should resume from the checkpoint
    // And "ProjectA" should not be re-analysed
    // And the final event stream should include all projects

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DiscoverDependenciesAsync_WhenProjectAAlreadyCompleted_SkipsProjectAAndYieldsHeartbeat()
    {
        // Arrange — two projects, ProjectA already completed
        const string projectA = "ProjectA";
        const string projectB = "ProjectB";

        var analysedProjects = new List<string>();
        var linkAnalysisService = new TrackingWorkItemLinkAnalysisService(analysedProjects);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService>("Simulated", linkAnalysisService);

        var catalogMock = new Mock<ICatalogService>(MockBehavior.Loose);
        services.AddSingleton(catalogMock.Object);

        var sp = services.BuildServiceProvider();

        var options = Options.Create(new MigrationPlatformOptions
        {
            Organisations =
            [
                new SimulatedOrganisationEntry
                {
                    Type = "Simulated",
                    Projects = [projectA, projectB],
                    Enabled = true
                }
            ]
        });

        var sut = new DependencyDiscoveryService(
            options, sp, catalogMock.Object,
            NullLogger<DependencyDiscoveryService>.Instance);

        // ProjectA was already fully analysed in the previous run
        var completedProjectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{SimulatedOrgUrl}|{projectA}"
        };

        // Act
        var events = new List<DependencyProgressEvent>();
        await foreach (var evt in sut.DiscoverDependenciesAsync(
            completedProjectKeys: completedProjectKeys,
            cancellationToken: CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — ProjectA was NOT re-analysed
        Assert.IsFalse(analysedProjects.Contains(projectA),
            "ProjectA should not be re-analysed when it appears in completedProjectKeys.");

        // Assert — ProjectB WAS analysed
        Assert.IsTrue(analysedProjects.Contains(projectB),
            "ProjectB should be analysed because it is not in completedProjectKeys.");

        // Assert — a skip heartbeat was emitted for ProjectA (representing resume checkpoint)
        var skippedHeartbeat = events.OfType<DependencyHeartbeatEvent>()
            .FirstOrDefault(h => h.IsComplete && h.ProjectName == projectA);
        Assert.IsNotNull(skippedHeartbeat,
            "A completed heartbeat should be emitted for the skipped project to represent checkpoint resume.");

        // Assert — event stream also contains ProjectB heartbeat
        var projectBHeartbeat = events.OfType<DependencyHeartbeatEvent>()
            .FirstOrDefault(h => h.ProjectName == projectB);
        Assert.IsNotNull(projectBHeartbeat,
            "Event stream should include a heartbeat for ProjectB.");
    }

    /// <summary>
    /// Fake <see cref="IWorkItemLinkAnalysisService"/> that records which projects were analysed
    /// and yields one heartbeat event per project.
    /// </summary>
    private sealed class TrackingWorkItemLinkAnalysisService : IWorkItemLinkAnalysisService
    {
        private readonly List<string> _analysedProjects;

        public TrackingWorkItemLinkAnalysisService(List<string> analysedProjects)
        {
            _analysedProjects = analysedProjects;
        }

        public async IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
            MigrationEndpointOptions endpoint,
            string project,
            string? wiqlFilter = null,
            BatchContinuationToken? savedContinuationToken = null,
            Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            IReadOnlyList<WorkItemFieldFilterOptions>? fieldFilters = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _analysedProjects.Add(project);

            yield return new DependencyHeartbeatEvent(
                OrganisationUrl: endpoint.GetResolvedUrl(),
                ProjectName: project,
                WorkItemsAnalysed: 0,
                ExternalLinksFound: 0,
                CrossProjectCount: 0,
                CrossOrgCount: 0,
                IsComplete: true);

            await Task.Yield();
        }
    }
}
