// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Factories;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.DependencyDiscovery;

[TestClass]
public sealed class SimulatedDependencyDiscoveryServiceFactoryTests
{
    private const string SimOrgUrl = "simulated://localhost";
    private const string ProjectName = "SimProject";

    private static IReadOnlyList<ScopedOrganisationEndpoint> OneOrg(string project = ProjectName)
        => new List<ScopedOrganisationEndpoint>
        {
            new()
            {
                Endpoint = new SimulatedEndpointOptions { Url = SimOrgUrl },
                Projects = new List<string> { project }
            }
        };

    // ── T024: factory can be instantiated and Create returns a service ─────
    // TODO: [test-validity] Score 8/25 — Rule 3 applied (sole coverage for Create() path).
    // Rewrite to verify service.DiscoverDependenciesAsync actually delegates to the link service
    // rather than just asserting non-null. E.g. inject a counting stub and assert it was called.
    [TestMethod]
    public void Create_WithSimulatedLinkService_ReturnsIDependencyDiscoveryService()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);

        var service = factory.Create(OneOrg(), new JobPolicies());

        Assert.IsNotNull(service, "Create must return a non-null IDependencyDiscoveryService");
    }

    // ── T024: service delegates to SimulatedWorkItemLinkAnalysisService ────
    [TestMethod]
    public async Task DiscoverDependenciesAsync_WithSimulatedConnector_ReturnsEmptySequenceWithoutNetworkCall()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);
        var service = factory.CreateForProject(OneOrg(), SimOrgUrl, ProjectName, new JobPolicies());

        var events = new List<DependencyProgressEvent>();
        await foreach (var evt in service.DiscoverDependenciesAsync(cancellationToken: CancellationToken.None))
        {
            events.Add(evt);
        }

        // SimulatedWorkItemLinkAnalysisService returns no links.
        // The simulated connector emits one completion heartbeat per project for progress visibility.
        var linkEvents = events.OfType<DependencyFoundEvent>().ToList();
        var heartbeats = events.OfType<DependencyHeartbeatEvent>().ToList();
        Assert.AreEqual(0, linkEvents.Count, "Simulated connector must yield no DependencyFoundEvent (no real links)");
        Assert.AreEqual(1, heartbeats.Count, "Simulated connector must emit exactly one completion heartbeat per project");
        Assert.IsTrue(heartbeats[0].IsComplete, "Completion heartbeat must have IsComplete=true");
    }

    // ── T024: factory resolves without external connectivity ──────────────
    [TestMethod]
    public void SimulatedFactory_CanBeResolvedFromKeyedDI_WithoutExternalConnectivity()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IWorkItemLinkAnalysisService, SimulatedWorkItemLinkAnalysisService>(
            serviceKey: "Simulated");
        services.AddSingleton<IDependencyDiscoveryServiceFactory, SimulatedDependencyDiscoveryServiceFactory>();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDependencyDiscoveryServiceFactory>();

        Assert.IsNotNull(factory);
        Assert.IsInstanceOfType(factory, typeof(SimulatedDependencyDiscoveryServiceFactory));
    }

    // ── T024: CreateForProject scopes to single project ───────────────────
    // TODO: [test-validity] Score 9/25 — Assert.IsNotNull(events) is a tautology (new List<T>() is never null).
    // Scoping behaviour (only 1 org's links discovered) is not actually verified.
    // Rewrite: inject a stub IWorkItemLinkAnalysisService that records which projects were requested,
    // then assert only "ProjectA" was requested (not ProjectB or ProjectC).
    [TestMethod]
    public async Task CreateForProject_ScopesDiscoveryToSingleProjectOnly()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);

        var multiOrg = new List<ScopedOrganisationEndpoint>
        {
            new()
            {
                Endpoint = new SimulatedEndpointOptions { Url = SimOrgUrl },
                Projects = new List<string> { "ProjectA", "ProjectB", "ProjectC" }
            }
        };

        var service = factory.CreateForProject(multiOrg, SimOrgUrl, "ProjectA", new JobPolicies());

        // Should not throw and should complete without network calls
        var events = new List<DependencyProgressEvent>();
        await foreach (var evt in service.DiscoverDependenciesAsync(cancellationToken: CancellationToken.None))
        {
            events.Add(evt);
        }

        // Simulated returns no links; one completion heartbeat is emitted per project.
        var linkEvents = events.OfType<DependencyFoundEvent>().ToList();
        var heartbeats = events.OfType<DependencyHeartbeatEvent>().ToList();
        Assert.AreEqual(0, linkEvents.Count, "Simulated factory CreateForProject must yield no link events");
        Assert.AreEqual(1, heartbeats.Count, "Simulated factory CreateForProject must emit one completion heartbeat");
        Assert.IsTrue(heartbeats[0].IsComplete, "Completion heartbeat must have IsComplete=true");
    }

    [TestMethod]
    public void CreateForProject_UnknownOrg_ThrowsInvalidOperationException()
    {
        var linkService = new SimulatedWorkItemLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            factory.CreateForProject(OneOrg(), "simulated://other-org", ProjectName, new JobPolicies()));

        StringAssert.Contains(ex.Message, "No simulated organisation matched");
    }

    [TestMethod]
    public async Task DiscoverDependenciesAsync_InProgressProject_ForwardsContinuationTokenAndWriter()
    {
        var linkService = new RecordingLinkAnalysisService();
        var factory = new SimulatedDependencyDiscoveryServiceFactory(linkService);
        var service = factory.CreateForProject(OneOrg(), SimOrgUrl, ProjectName, new JobPolicies());
        var token = new BatchContinuationToken
        {
            ChangedDateUtc = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
            WorkItemId = 42,
            QueryFingerprint = "fingerprint",
            GeneratedAtUtc = new System.DateTime(2026, 1, 1, 0, 1, 0, System.DateTimeKind.Utc)
        };
        var writerCalled = false;

        async Task CheckpointWriter(BatchContinuationToken _, CancellationToken __)
        {
            writerCalled = true;
            await Task.CompletedTask;
        }

        await foreach (var _ in service.DiscoverDependenciesAsync(
            inProgressProjectKey: $"{SimOrgUrl}|{ProjectName}",
            inProgressToken: token,
            continuationCheckpointWriter: CheckpointWriter,
            cancellationToken: CancellationToken.None))
        {
        }

        Assert.IsNotNull(linkService.LastToken);
        Assert.AreEqual(token.WorkItemId, linkService.LastToken!.WorkItemId);
        Assert.IsNotNull(linkService.LastWriter);
        await linkService.LastWriter!(token, CancellationToken.None);
        Assert.IsTrue(writerCalled, "Continuation checkpoint writer must be forwarded to the link analysis service.");
    }

    private sealed class RecordingLinkAnalysisService : IWorkItemLinkAnalysisService
    {
        public BatchContinuationToken? LastToken { get; private set; }
        public Func<BatchContinuationToken, CancellationToken, Task>? LastWriter { get; private set; }

        public async IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
            MigrationEndpointOptions endpoint,
            string project,
            string? wiqlFilter = null,
            BatchContinuationToken? savedContinuationToken = null,
            Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastToken = savedContinuationToken;
            LastWriter = continuationCheckpointWriter;
            await Task.CompletedTask;

            yield return new DependencyHeartbeatEvent(
                OrganisationUrl: endpoint.GetResolvedUrl(),
                ProjectName: project,
                WorkItemsAnalysed: 0,
                ExternalLinksFound: 0,
                CrossProjectCount: 0,
                CrossOrgCount: 0,
                IsComplete: true);
        }
    }
}
