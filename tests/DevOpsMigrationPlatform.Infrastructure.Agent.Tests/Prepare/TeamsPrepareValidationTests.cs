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
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Prepare;

/// <summary>
/// MC-L1 contract tests: Teams Prepare performs real validation of the exported team
/// artefacts (<c>Teams/{slug}/team.json</c> and split artefacts) and records findings
/// in <c>Teams/prepare-report.json</c> using the same finding shapes/severities as the
/// WorkItems Prepare path. Includes the cross-module reference check of team-member
/// descriptors against the Identities export (<c>Identities/descriptors.jsonl</c>).
/// The package format is connector-neutral, so these checks cover all three connectors.
/// </summary>
[TestClass]
public sealed class TeamsPrepareValidationTests
{
    private const string Org = "testorg";
    private const string Project = "testproject";

    private const string ValidTeamJson =
        """
        {
          "definition": { "id": "team-1", "name": "Team Alpha", "description": "", "isDefault": true },
          "members": []
        }
        """;

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_ValidTeamArtefacts_ReportsResolvedTeamsAndNoFindings()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-alpha", "team.json", ValidTeamJson);
        await SeedTeamArtefactAsync(package, "team-alpha", "members.json",
            """[ { "descriptor": "aad.user1", "displayName": "User One", "uniqueName": "one@example.test", "isAdmin": false } ]""");
        await SeedIdentitiesDescriptorsAsync(package,
            """{ "descriptor": "aad.user1", "displayName": "User One", "uniqueName": "one@example.test", "sourceType": "user", "origin": "test", "isActive": true }""");

        var report = await RunPrepareAsync(package);

        Assert.AreEqual(1, report.ResolvedCount, "The single valid team should be resolved.");
        Assert.AreEqual(0, report.UnresolvedCount, $"Expected no findings but got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_MalformedTeamJson_ReportsBlockingInvalidArtefactFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-broken", "team.json", "{ not valid json !!");

        var report = await RunPrepareAsync(package);

        Assert.AreEqual(0, report.ResolvedCount);
        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Blocking
                && i.Key.Contains("team-broken", StringComparison.OrdinalIgnoreCase)),
            $"Expected a blocking finding for the malformed team.json. Got: {Describe(report)}");
        Assert.IsTrue(report.ArtefactFindings.Any(f => f.Status == Abstractions.Agent.WorkItems.ArtefactFindingStatus.Invalid),
            "Expected an Invalid artefact finding.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_TeamJsonMissingDefinition_ReportsBlockingFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-nodef", "team.json", """{ "members": [] }""");

        var report = await RunPrepareAsync(package);

        Assert.AreEqual(0, report.ResolvedCount);
        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Blocking
                && i.Reason.Contains("definition", StringComparison.OrdinalIgnoreCase)),
            $"Expected a blocking missing-definition finding. Got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_DuplicateTeamNames_ReportsWarningFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-alpha", "team.json", ValidTeamJson);
        await SeedTeamArtefactAsync(package, "team-alpha-2", "team.json",
            """
            {
              "definition": { "id": "team-2", "name": "team alpha", "description": "", "isDefault": false },
              "members": []
            }
            """);

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Warning
                && i.Reason.Contains("duplicate", StringComparison.OrdinalIgnoreCase)),
            $"Expected a duplicate team-name warning. Got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_MemberDescriptorMissingFromIdentitiesExport_ReportsWarningFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-alpha", "team.json", ValidTeamJson);
        await SeedTeamArtefactAsync(package, "team-alpha", "members.json",
            """[ { "descriptor": "aad.ghost", "displayName": "Ghost", "uniqueName": "ghost@example.test", "isAdmin": false } ]""");
        await SeedIdentitiesDescriptorsAsync(package,
            """{ "descriptor": "aad.user1", "displayName": "User One", "uniqueName": "one@example.test", "sourceType": "user", "origin": "test", "isActive": true }""");

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Warning
                && i.Key.Contains("aad.ghost", StringComparison.OrdinalIgnoreCase)),
            $"Expected a warning for the unmatched member descriptor. Got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_BoardConfigWithEmptyColumns_ReportsWarningFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-alpha", "team.json", ValidTeamJson);
        await SeedTeamArtefactAsync(package, "team-alpha", "board-config.json",
            """
            {
              "teamName": "Team Alpha",
              "boards": [ { "boardName": "Stories", "columns": [], "swimLanes": [] } ]
            }
            """);

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Warning
                && i.Reason.Contains("column", StringComparison.OrdinalIgnoreCase)),
            $"Expected a warning for the empty board column set. Got: {Describe(report)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PrepareAsync_MalformedSplitArtefact_ReportsBlockingFinding()
    {
        using var package = new InMemoryPackageAccess();
        await SeedTeamArtefactAsync(package, "team-alpha", "team.json", ValidTeamJson);
        await SeedTeamArtefactAsync(package, "team-alpha", "settings.json", "not-json{{");

        var report = await RunPrepareAsync(package);

        Assert.IsTrue(
            report.UnresolvedItems.Any(i =>
                i.Severity == PrepareIssueSeverity.Blocking
                && i.Key.Contains("settings.json", StringComparison.OrdinalIgnoreCase)),
            $"Expected a blocking finding for the malformed settings artefact. Got: {Describe(report)}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<PrepareReport> RunPrepareAsync(InMemoryPackageAccess package)
    {
        var orchestrator = new TeamsOrchestrator(NullLogger<TeamsOrchestrator>.Instance, importOrchestrator: null);

        var context = new PrepareContext
        {
            Job = new Job { JobId = "job-teams-prepare", Kind = JobKind.Prepare },
            Package = package
        };

        await orchestrator.PrepareAsync(context, Org, Project, CancellationToken.None);

        return await ReadReportAsync(package);
    }

    private static async Task SeedTeamArtefactAsync(InMemoryPackageAccess package, string slug, string fileName, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: Org,
                Project: Project,
                Module: "Teams",
                Address: new RelativePathAddress($"{slug}/{fileName}")),
            new PackagePayload(stream, "application/json"),
            CancellationToken.None);
    }

    private static async Task SeedIdentitiesDescriptorsAsync(InMemoryPackageAccess package, params string[] descriptorLines)
    {
        var content = string.Join("\n", descriptorLines) + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: Org,
                Project: Project,
                Module: "Identities",
                Address: new RelativePathAddress("descriptors.jsonl")),
            new PackagePayload(stream, "application/x-ndjson"),
            CancellationToken.None);
    }

    private static async Task<PrepareReport> ReadReportAsync(InMemoryPackageAccess package)
    {
        var payload = await package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: Org,
                Project: Project,
                Module: "Teams",
                Address: new RelativePathAddress("prepare-report.json")),
            CancellationToken.None);

        Assert.IsNotNull(payload, "Teams/prepare-report.json should be written by PrepareAsync.");
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
