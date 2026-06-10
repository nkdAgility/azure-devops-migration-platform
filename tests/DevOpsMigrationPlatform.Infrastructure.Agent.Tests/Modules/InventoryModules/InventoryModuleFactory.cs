// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using System.Text;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;

/// <summary>
/// Creates fully-wired module instances using minimal stubs.
/// Each factory method mirrors the corresponding <c>*ModuleInventoryTests.CreateModule()</c>
/// pattern but wires the inventory orchestrator to actually write artefacts, so that
/// the multi-module integration scenario can assert production across all four modules.
/// </summary>
internal static class InventoryModuleFactory
{
    private const string OrgUrl     = "https://source.example";
    private const string ProjectName = "ProjectA";

    // --- per-module factories ---

    internal static WorkItemsModule CreateWorkItemsModule(Mock<IPackageAccess> packageMock)
    {
        var sourceEndpoint = CreateSourceEndpointMock();
        sourceEndpoint.SetupGet(s => s.OrganisationSlug).Returns("source-example");

        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns(ProjectName);
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");
        targetEndpoint.SetupGet(s => s.OrganisationSlug).Returns("target-example");

        // The orchestrator mock drains the event stream and merges inventory counts.
        // This simulates the real InventoryOrchestrator writing to the package.
        var orchestrator = new Mock<IInventoryOrchestrator>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.RunAsync(
                "WorkItems",
                It.IsAny<IAsyncEnumerable<InventoryProgressEvent>>(),
                It.IsAny<InventoryContext>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IAsyncEnumerable<InventoryProgressEvent>, InventoryContext, int, CancellationToken>(
                async (_, stream, ctx, _, ct) =>
                {
                    // Drain the event stream to satisfy WorkItemsModule's internal pipeline.
                    await foreach (var _ in stream.WithCancellation(ct).ConfigureAwait(false)) { }

                    // Simulate the per-project inventory write that InventoryOrchestrator performs.
                    var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(OrgUrl);
                    var payload = JsonSerializer.Serialize(
                        new { generatedAt = DateTimeOffset.UtcNow, workItems = 2, project = ctx.Project },
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    using var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(payload), writable: false);
                    await ctx.Package.PersistIndexAsync(
                        new PackageIndexContext("inventory.json", Organisation: orgSlug, Project: ctx.Project),
                        new PackagePayload(ms, "application/json"),
                        ct).ConfigureAwait(false);
                });

        var discovery = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        discovery
            .Setup(d => d.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                ProjectName,
                It.IsAny<WorkItemFetchScope?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(StubWorkItemSummaries());

        return new WorkItemsModule(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            NullLogger<WorkItemsModule>.Instance,
            Options.Create(new WorkItemsModuleOptions()),
            sourceEndpoint.Object,
            NullLogger<WorkItemsImportRuntime>.Instance,
            Mock.Of<IWorkItemTargetFactory>(),
            Mock.Of<IWorkItemResolutionStrategyFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IWorkItemResolutionProcessorFactory>(),
            targetEndpoint.Object,
            identityMappingService: Mock.Of<IIdentityMappingService>(),
            nodeTranslationTool: Mock.Of<INodeTranslationTool>(),
            fieldTransformTool: Mock.Of<IFieldTransformTool>(),
            fetchService: null,
            inventoryOrchestrator: orchestrator.Object,
            PlatformMetrics: null,
            discoveryService: discovery.Object);
    }

    internal static NodesModule CreateNodesModule()
    {
        var sourceEndpoint = CreateSourceEndpointMock();

        var reader = new Mock<IClassificationTreeReader>(MockBehavior.Strict);
        reader.Setup(r => r.CountNodesAsync(ProjectName, It.IsAny<CancellationToken>()))
              .ReturnsAsync(3);

        return new NodesModule(
            NullLogger<NodesModule>.Instance,
            Options.Create(new NodesModuleOptions { Enabled = true }),
            sourceEndpoint.Object,
            new NodesOrchestrator(
                NullLogger<NodesOrchestrator>.Instance,
                Mock.Of<INodeTranslationTool>(),
                Mock.Of<INodeCreator>(),
                CreateNodeTranslationOptions()),
            PlatformMetrics: null,
            capture: null,
            targetEndpointInfo: Mock.Of<ITargetEndpointInfo>(),
            reader: reader.Object);
    }

    internal static IdentitiesModule CreateIdentitiesModule()
    {
        var sourceEndpoint = CreateSourceEndpointMock();

        return new IdentitiesModule(
            NullLogger<IdentitiesModule>.Instance,
            Options.Create(new IdentitiesModuleOptions { Enabled = true }),
            sourceEndpoint.Object,
            new IdentitiesOrchestrator(NullLogger<IdentitiesOrchestrator>.Instance),
            PlatformMetrics: null,
            identitySource: new StubIdentitySource());
    }

    internal static TeamsModule CreateTeamsModule()
    {
        var sourceEndpoint = CreateSourceEndpointMock();

        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns(ProjectName);
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(t => t.EnumerateTeamsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(StubTeams());

        return new TeamsModule(
            NullLogger<TeamsModule>.Instance,
            Options.Create(new TeamsModuleOptions { Enabled = true }),
            sourceEndpoint.Object,
            targetEndpoint.Object,
            Mock.Of<ITeamsOrchestrator>(),
            PlatformMetrics: null,
            teamSource: teamSource.Object,
            teamTarget: Mock.Of<ITeamTarget>());
    }

    internal static InventoryAnalyser CreateInventoryAnalyser()
        => new(NullLogger<InventoryAnalyser>.Instance);

    // --- helpers ---

    /// <summary>
    /// Creates a source-endpoint mock pre-configured with the shared test org URL,
    /// project name, and connector type. Callers may add extra setups (e.g. OrganisationSlug).
    /// </summary>
    private static Mock<ISourceEndpointInfo> CreateSourceEndpointMock()
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(s => s.Project).Returns(ProjectName);
        mock.SetupGet(s => s.Url).Returns(OrgUrl);
        mock.SetupGet(s => s.ConnectorType).Returns("Simulated");
        return mock;
    }

    private static IOptionsMonitor<NodeTranslationOptions> CreateNodeTranslationOptions()
    {
        var options = new Mock<IOptionsMonitor<NodeTranslationOptions>>(MockBehavior.Loose);
        options.SetupGet(o => o.CurrentValue).Returns(new NodeTranslationOptions());
        return options.Object;
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> StubWorkItemSummaries()
    {
        yield return new ProjectDiscoverySummary
        {
            ProjectName = ProjectName,
            WorkItemsCount = 2,
            RevisionsCount = 4,
            IsWorkItemComplete = true
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<TeamDefinition> StubTeams()
    {
        yield return new TeamDefinition("1", "Team One", "One", false);
        await Task.Yield();
        yield return new TeamDefinition("2", "Team Two", "Two", false);
    }

    private sealed class StubIdentitySource : IIdentitySource
    {
        public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
            string projectName,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new IdentityDescriptor("d1", "User One", "user1@example.com", "User", "Simulated", true);
            await Task.Yield();
            yield return new IdentityDescriptor("d2", "User Two", "user2@example.com", "User", "Simulated", true);
        }
    }
}
