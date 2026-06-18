// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// Dispatch tests for the <see cref="TeamsOrchestrator"/> extension seam.
/// These tests verify that the orchestrator correctly filters, orders, and invokes
/// <see cref="IModuleExtension"/> instances per team by constructing a real
/// TeamsOrchestrator and asserting on captured extension calls.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public class TeamExtensionDispatchTests
{
    // ---------------------------------------------------------------------------
    // Spy
    // ---------------------------------------------------------------------------

    private sealed class SpyExtension : IModuleExtension
    {
        public SpyExtension(
            string name,
            int order,
            bool supportsExport = true,
            bool supportsImport = true,
            bool isEnabled = true)
        {
            Name = name;
            Order = order;
            SupportsExport = supportsExport;
            SupportsImport = supportsImport;
            IsEnabled = isEnabled;
        }

        public string Module => "Teams";
        public string Name { get; }
        public int Order { get; }
        public bool SupportsExport { get; }
        public bool SupportsImport { get; }
        public bool IsEnabled { get; }

        public List<string> ExportCalls { get; } = new();
        public List<string> ImportCalls { get; } = new();

        public Task ExportAsync(IExtensionContext ctx, CancellationToken ct)
        {
            ExportCalls.Add(Name);
            return Task.CompletedTask;
        }

        public Task ImportAsync(IExtensionContext ctx, CancellationToken ct)
        {
            ImportCalls.Add(Name);
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------------------
    // Builder helpers
    // ---------------------------------------------------------------------------

    private static Mock<ISourceEndpointInfo> BuildSourceEndpoint()
    {
        var m = new Mock<ISourceEndpointInfo>(MockBehavior.Loose);
        m.Setup(s => s.Project).Returns("TestProject");
        m.Setup(s => s.OrganisationSlug).Returns("test-org");
        return m;
    }

    private static Mock<ITargetEndpointInfo> BuildTargetEndpoint()
    {
        var m = new Mock<ITargetEndpointInfo>(MockBehavior.Loose);
        m.Setup(t => t.Project).Returns("TestProject");
        m.Setup(t => t.OrganisationSlug).Returns("test-org");
        return m;
    }

    private static Mock<IPackageAccess> BuildExportPackage()
    {
        var m = new Mock<IPackageAccess>(MockBehavior.Loose);
        m.Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(false);
        m.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
         .Returns(ValueTask.CompletedTask);
        return m;
    }

    private static async IAsyncEnumerable<TeamDefinition> YieldTeam(
        TeamDefinition team,
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        yield return team;
        await Task.CompletedTask;
    }

    private static Mock<ITeamSource> BuildTeamSource()
    {
        var team = new TeamDefinition("team-1", "Alpha Team", "", IsDefault: true);
        var m = new Mock<ITeamSource>(MockBehavior.Loose);
        m.Setup(s => s.EnumerateTeamsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .Returns(YieldTeam(team));
        return m;
    }

    private static TeamExportOrchestrator BuildTeamExportOrchestrator(Mock<ITeamSource> teamSource)
        => new(
            teamSource.Object,
            NullLogger<TeamExportOrchestrator>.Instance,
            BuildSourceEndpoint().Object);

    private static TeamsOrchestrator BuildOrchestrator(
        IEnumerable<IModuleExtension> extensions,
        Mock<IPackageAccess> package,
        Mock<ITeamSource> teamSource)
        => new(
            NullLogger<TeamsOrchestrator>.Instance,
            PlatformMetrics: null,
            exportOrchestrator: BuildTeamExportOrchestrator(teamSource),
            importOrchestrator: null,
            slugGenerator: new TeamSlugGenerator(),
            package: package.Object,
            extensions: extensions);

    private static ExportContext BuildExportContext(Mock<IPackageAccess> package)
        => new()
        {
            Job = new Abstractions.Jobs.Job { Kind = Abstractions.Jobs.JobKind.Export },
            Package = package.Object,
            ProgressSink = Mock.Of<IProgressSink>(),
        };

    private static TeamsModuleOptions ExportOptions()
        => new() { AlwaysExport = true };

    // ---------------------------------------------------------------------------
    // Import helpers
    // ---------------------------------------------------------------------------

    private static Mock<IPackageAccess> BuildImportPackage()
    {
        var teamPackage = new TeamPackage
        {
            Definition = new TeamDefinition("team-1", "Alpha Team", "", IsDefault: true),
        };
        var json = JsonSerializer.Serialize(teamPackage,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = Encoding.UTF8.GetBytes(json);

        var m = new Mock<IPackageAccess>(MockBehavior.Loose);

        m.Setup(p => p.EnumerateContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
         .Returns(YieldPath("Teams/alpha-team/team.json"));

        m.Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(() => new PackagePayload(new System.IO.MemoryStream(bytes, writable: false)));

        m.Setup(p => p.ContentExistsAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(false);

        m.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
         .Returns(ValueTask.CompletedTask);

        return m;
    }

    private static async IAsyncEnumerable<string> YieldPath(
        string path,
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        yield return path;
        await Task.CompletedTask;
    }

    private static Mock<ITeamTarget> BuildTeamTarget()
        => new Mock<ITeamTarget>(MockBehavior.Loose);

    private static TeamImportOrchestrator BuildTeamImportOrchestrator()
        => new(
            BuildTeamTarget().Object,
            NullLogger<TeamImportOrchestrator>.Instance,
            BuildTargetEndpoint().Object);

    private static TeamsOrchestrator BuildOrchestratorForImport(
        IEnumerable<IModuleExtension> extensions,
        Mock<IPackageAccess> package)
        => new(
            NullLogger<TeamsOrchestrator>.Instance,
            PlatformMetrics: null,
            exportOrchestrator: null,
            importOrchestrator: BuildTeamImportOrchestrator(),
            slugGenerator: new TeamSlugGenerator(),
            package: package.Object,
            extensions: extensions);

    private static ImportContext BuildImportContext(Mock<IPackageAccess> package)
        => new()
        {
            Job = new Abstractions.Jobs.Job { Kind = Abstractions.Jobs.JobKind.Import },
            Package = package.Object,
            ProgressSink = Mock.Of<IProgressSink>(),
        };

    // ---------------------------------------------------------------------------
    // (a) TeamsOrchestrator.ExportAsync dispatches to enabled export extensions
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_CallsExtension_WhenEnabledAndSupportsExport()
    {
        var spy = new SpyExtension("Settings", order: 10, supportsExport: true, isEnabled: true);
        var package = BuildExportPackage();
        var teamSource = BuildTeamSource();
        var orchestrator = BuildOrchestrator([spy], package, teamSource);

        await orchestrator.ExportAsync(
            teamSource.Object,
            BuildExportContext(package),
            BuildSourceEndpoint().Object,
            checkpointingFactory: null,
            ExportOptions(),
            CancellationToken.None);

        Assert.AreEqual(1, spy.ExportCalls.Count,
            "TeamsOrchestrator must call ExportAsync on an enabled export extension.");
    }

    // ---------------------------------------------------------------------------
    // (b) TeamsOrchestrator.ExportAsync skips extensions with IsEnabled=false
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_DoesNotCallExtension_WhenIsEnabledFalse()
    {
        var spy = new SpyExtension("Settings", order: 10, supportsExport: true, isEnabled: false);
        var package = BuildExportPackage();
        var teamSource = BuildTeamSource();
        var orchestrator = BuildOrchestrator([spy], package, teamSource);

        await orchestrator.ExportAsync(
            teamSource.Object,
            BuildExportContext(package),
            BuildSourceEndpoint().Object,
            checkpointingFactory: null,
            ExportOptions(),
            CancellationToken.None);

        Assert.AreEqual(0, spy.ExportCalls.Count,
            "TeamsOrchestrator must not call ExportAsync on a disabled extension.");
    }

    // ---------------------------------------------------------------------------
    // (c) TeamsOrchestrator.ImportAsync skips extensions with SupportsImport=false
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_DoesNotCallExtension_WhenSupportsImportFalse()
    {
        var callSpy   = new SpyExtension("ImportEnabled",  order: 10, supportsImport: true,  isEnabled: true);
        var skipSpy   = new SpyExtension("ImportDisabled", order: 20, supportsImport: false, isEnabled: true);
        var package   = BuildImportPackage();
        var orchestrator = BuildOrchestratorForImport([callSpy, skipSpy], package);

        await orchestrator.ImportAsync(
            BuildImportContext(package),
            BuildSourceEndpoint().Object,
            BuildTargetEndpoint().Object,
            checkpointingFactory: null,
            new TeamsModuleOptions(),
            CancellationToken.None);

        Assert.AreEqual(1, callSpy.ImportCalls.Count,
            "TeamsOrchestrator must call ImportAsync on extensions with SupportsImport=true.");
        Assert.AreEqual(0, skipSpy.ImportCalls.Count,
            "TeamsOrchestrator must not call ImportAsync on extensions with SupportsImport=false.");
    }

    // ---------------------------------------------------------------------------
    // (d) TeamsOrchestrator.ExportAsync invokes extensions in Order sequence
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_InvokesExtensions_InOrderSequence()
    {
        var ext1 = new SpyExtension("First",  order: 10, supportsExport: true, isEnabled: true);
        var ext2 = new SpyExtension("Second", order: 20, supportsExport: true, isEnabled: true);
        var ext3 = new SpyExtension("Third",  order: 5,  supportsExport: true, isEnabled: true);

        var recorded = new List<string>();
        // Capture call order via a single ordered list across all spies — done below via ExportCalls.
        // We pass them in deliberate non-sorted order; the orchestrator must sort by Order.
        var package    = BuildExportPackage();
        var teamSource = BuildTeamSource();
        var orchestrator = BuildOrchestrator([ext1, ext2, ext3], package, teamSource);

        await orchestrator.ExportAsync(
            teamSource.Object,
            BuildExportContext(package),
            BuildSourceEndpoint().Object,
            checkpointingFactory: null,
            ExportOptions(),
            CancellationToken.None);

        // Each spy records its own name once; verify each was called exactly once first.
        Assert.AreEqual(1, ext1.ExportCalls.Count, "First (Order=10) must be called");
        Assert.AreEqual(1, ext2.ExportCalls.Count, "Second (Order=20) must be called");
        Assert.AreEqual(1, ext3.ExportCalls.Count, "Third (Order=5) must be called");

        // To verify order, use a shared recording list instead.
        var ordered = new List<string>();
        var orderedExt1 = new OrderCapturingExtension("First",  order: 10, ordered);
        var orderedExt2 = new OrderCapturingExtension("Second", order: 20, ordered);
        var orderedExt3 = new OrderCapturingExtension("Third",  order: 5,  ordered);

        var package2    = BuildExportPackage();
        var teamSource2 = BuildTeamSource();
        var orchestrator2 = BuildOrchestrator(
            [orderedExt1, orderedExt2, orderedExt3], package2, teamSource2);

        await orchestrator2.ExportAsync(
            teamSource2.Object,
            BuildExportContext(package2),
            BuildSourceEndpoint().Object,
            checkpointingFactory: null,
            ExportOptions(),
            CancellationToken.None);

        Assert.AreEqual(3, ordered.Count, "All 3 extensions must be invoked");
        Assert.AreEqual("Third",  ordered[0], "Order=5 must run first");
        Assert.AreEqual("First",  ordered[1], "Order=10 must run second");
        Assert.AreEqual("Second", ordered[2], "Order=20 must run last");
    }

    private sealed class OrderCapturingExtension : IModuleExtension
    {
        private readonly List<string> _sink;

        public OrderCapturingExtension(string name, int order, List<string> sink)
        {
            Name = name;
            Order = order;
            _sink = sink;
        }

        public string Module => "Teams";
        public string Name { get; }
        public int Order { get; }
        public bool SupportsExport => true;
        public bool SupportsImport => true;
        public bool IsEnabled => true;

        public Task ExportAsync(IExtensionContext ctx, CancellationToken ct) { _sink.Add(Name); return Task.CompletedTask; }
        public Task ImportAsync(IExtensionContext ctx, CancellationToken ct) { _sink.Add(Name); return Task.CompletedTask; }
    }
}
