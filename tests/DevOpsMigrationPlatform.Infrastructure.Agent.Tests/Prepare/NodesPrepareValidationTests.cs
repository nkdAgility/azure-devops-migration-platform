// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Prepare;

/// <summary>
/// MC-L1 contract tests: Nodes Prepare performs real validation of the exported
/// classification-tree artefact (<c>Nodes/source-tree.json</c>) and records findings
/// in <c>Nodes/prepare-report.json</c> using the same finding shapes/severities as
/// the WorkItems Prepare path (<see cref="UnresolvedItem"/> / <see cref="PrepareReport"/>).
/// The package format is connector-neutral, so these checks cover all three connectors.
/// </summary>
[TestClass]
public sealed class NodesPrepareValidationTests
{
    private const string Org = "testorg";
    private const string Project = "testproject";

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_ValidSourceTree_ReportsResolvedNodesAndNoFindings()
    {
        using var package = new InMemoryPackageAccess();
        await SeedSourceTreeAsync(package,
            """
            {
              "areaNodes": ["testproject\\Area1", "testproject\\Area2"],
              "iterationNodes": [
                { "path": "testproject\\Sprint 1", "startDate": "2026-01-01T00:00:00Z", "finishDate": "2026-01-14T00:00:00Z", "isBacklogIteration": false },
                { "path": "testproject\\Sprint 2", "isBacklogIteration": false }
              ]
            }
            """);

        var report = await RunPrepareAsync(package);

        Assert.AreEqual(4, report.ResolvedCount, "All four nodes should be resolved.");
        Assert.AreEqual(0, report.UnresolvedCount, $"Expected no findings but got: {Describe(report)}");
        Assert.AreEqual(0, report.ArtefactFindings.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_MissingSourceTree_ReportsBlockingMissingArtefactFinding()
    {
        using var package = new InMemoryPackageAccess();

        var report = await RunPrepareAsync(package);

        Assert.AreEqual(0, report.ResolvedCount);
        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Blocking
                && i.Key.Contains("source-tree.json", StringComparison.OrdinalIgnoreCase)),
            $"Expected a blocking finding for the missing source-tree artefact. Got: {Describe(report)}");
        Assert.IsTrue(report.ArtefactFindings.Any(f => f.Status == Abstractions.Agent.WorkItems.ArtefactFindingStatus.Missing),
            "Expected a Missing artefact finding.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_MalformedSourceTree_ReportsBlockingInvalidArtefactFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedSourceTreeAsync(package, "{ not valid json !!");

        var report = await RunPrepareAsync(package);

        Assert.AreEqual(0, report.ResolvedCount);
        Assert.IsTrue(
            report.UnresolvedItems.Any(i => i.Severity == PrepareIssueSeverity.Blocking),
            $"Expected a blocking finding for the malformed artefact. Got: {Describe(report)}");
        Assert.IsTrue(report.ArtefactFindings.Any(f => f.Status == Abstractions.Agent.WorkItems.ArtefactFindingStatus.Invalid),
            "Expected an Invalid artefact finding.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_DuplicateNodePaths_ReportsWarningFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedSourceTreeAsync(package,
            """
            {
              "areaNodes": ["testproject\\Area1", "testproject\\area1"],
              "iterationNodes": []
            }
            """);

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Warning
                && i.Reason.Contains("duplicate", StringComparison.OrdinalIgnoreCase)),
            $"Expected a duplicate-path warning. Got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_IterationStartAfterFinish_ReportsWarningFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedSourceTreeAsync(package,
            """
            {
              "areaNodes": [],
              "iterationNodes": [
                { "path": "testproject\\Sprint 1", "startDate": "2026-02-01T00:00:00Z", "finishDate": "2026-01-01T00:00:00Z", "isBacklogIteration": false }
              ]
            }
            """);

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Warning
                && i.Key.Contains("Sprint 1", StringComparison.OrdinalIgnoreCase)),
            $"Expected an iteration date-order warning for 'Sprint 1'. Got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_EmptyNodePath_ReportsBlockingFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedSourceTreeAsync(package,
            """
            {
              "areaNodes": ["   "],
              "iterationNodes": []
            }
            """);

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i => i.Severity == PrepareIssueSeverity.Blocking),
            $"Expected a blocking finding for the empty node path. Got: {Describe(report)}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<PrepareReport> RunPrepareAsync(InMemoryPackageAccess package)
    {
        var orchestrator = new NodesOrchestrator(
            NullLogger<NodesOrchestrator>.Instance,
            Mock.Of<INodeTranslationTool>(),
            Mock.Of<INodeCreator>());

        var context = new PrepareContext
        {
            Job = new Job { JobId = "job-nodes-prepare", Kind = JobKind.Prepare },
            Package = package
        };

        await orchestrator.PrepareAsync(context, Org, Project, CancellationToken.None);

        return await ReadReportAsync(package);
    }

    private static async Task SeedSourceTreeAsync(InMemoryPackageAccess package, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: Org,
                Project: Project,
                Module: "Nodes",
                Address: new RelativePathAddress("source-tree.json")),
            new PackagePayload(stream, "application/json"),
            CancellationToken.None);
    }

    private static async Task<PrepareReport> ReadReportAsync(InMemoryPackageAccess package)
    {
        var payload = await package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: Org,
                Project: Project,
                Module: "Nodes",
                Address: new RelativePathAddress("prepare-report.json")),
            CancellationToken.None);

        Assert.IsNotNull(payload, "Nodes/prepare-report.json should be written by PrepareAsync.");
        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var report = JsonSerializer.Deserialize<PrepareReport>(json);
        Assert.IsNotNull(report);
        return report;
    }

    private static string Describe(PrepareReport report)
        => string.Join("; ", report.UnresolvedItems.Select(i => $"{i.Severity}:{i.Key}:{i.Reason}"));
}
