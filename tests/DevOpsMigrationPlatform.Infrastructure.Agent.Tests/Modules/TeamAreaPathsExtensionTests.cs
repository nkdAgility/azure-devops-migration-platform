// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Behavioural tests for <see cref="TeamAreaPathsTeamExtension"/> against the API-shaped
/// area path model: export captures <c>includeChildren</c> from the source connector into
/// <c>Teams/{slug}/area-paths.json</c>; import replays it verbatim, accepts the legacy
/// bare-string package shape, and never area-path-translates custom team fields.
/// </summary>
[TestClass]
public sealed class TeamAreaPathsExtensionTests
{
    private static TeamExtensionContext CreateContext(
        IPackageAccess package,
        string? targetEntityId = null)
        => new()
        {
            Organisation = "org",
            ProjectName = "Proj",
            EntityId = "team-1",
            TargetEntityId = targetEntityId,
            Package = package,
            Team = new TeamDefinition("team-1", "Alpha Team", string.Empty, false),
            Slug = "alpha-team",
            SourceProjectName = "Proj",
        };

    private static Mock<IPackageAccess> CreateWriteCapturingPackage(
        List<string> writtenPaths, List<string> writtenJsons)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package.Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((ctx, payload, _) =>
            {
                writtenPaths.Add(ctx.Address?.RelativePath ?? string.Empty);
                payload.Content.Position = 0;
                writtenJsons.Add(new StreamReader(payload.Content, Encoding.UTF8).ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);
        return package;
    }

    private static Mock<IPackageAccess> CreateReadOnlyPackage(string areaPathsJson)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("area-paths.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(areaPathsJson))));
        return package;
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_WritesAreaPathsJson_CapturingIncludeChildren()
    {
        // Arrange — source reports one 'include sub areas' and one 'exclude sub areas' entry
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamAreaPathsAsync("Proj", "team-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamAreaPaths(
                "Proj",
                new List<TeamFieldValueEntry>
                {
                    new("Proj", IncludeChildren: true),
                    new("Proj\\Sub", IncludeChildren: false),
                }));

        var writtenPaths = new List<string>();
        var writtenJsons = new List<string>();
        var package = CreateWriteCapturingPackage(writtenPaths, writtenJsons);

        var extension = new TeamAreaPathsTeamExtension(
            TestConnectorCapabilities.All,
            teamSource.Object,
            new Mock<ITeamTarget>(MockBehavior.Loose).Object);

        // Act
        Assert.IsTrue(extension.SupportsExport, "area paths must be captured on export");
        await extension.ExportAsync(CreateContext(package.Object), CancellationToken.None);

        // Assert — area-paths.json written in the API shape with includeChildren verbatim
        var index = writtenPaths.FindIndex(p => p.Replace('\\', '/').EndsWith("area-paths.json"));
        Assert.IsTrue(index >= 0, "Expected Teams/alpha-team/area-paths.json to be written.");
        using var doc = JsonDocument.Parse(writtenJsons[index]);
        var root = doc.RootElement;
        Assert.AreEqual("Proj", root.GetProperty("defaultValue").GetString());
        var values = root.GetProperty("values");
        Assert.AreEqual(2, values.GetArrayLength());
        Assert.IsTrue(values[0].GetProperty("includeChildren").GetBoolean());
        Assert.AreEqual("Proj\\Sub", values[1].GetProperty("value").GetString());
        Assert.IsFalse(values[1].GetProperty("includeChildren").GetBoolean(),
            "'Exclude sub areas' must be captured, not silently widened");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_WritesNothing_WhenSourceReturnsNull()
    {
        // Arrange — e.g. the TFS Object Model connector cannot read team field values
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamAreaPathsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamAreaPaths?)null);

        var writtenPaths = new List<string>();
        var package = CreateWriteCapturingPackage(writtenPaths, new List<string>());

        var extension = new TeamAreaPathsTeamExtension(
            TestConnectorCapabilities.All,
            teamSource.Object,
            new Mock<ITeamTarget>(MockBehavior.Loose).Object);

        // Act
        await extension.ExportAsync(CreateContext(package.Object), CancellationToken.None);

        // Assert
        Assert.AreEqual(0, writtenPaths.Count, "no artefact should be written when the source has no area paths");
    }

    // ── Import ────────────────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_ReplaysIncludeChildrenVerbatim()
    {
        // Arrange — API-shaped area-paths.json with a mixed includeChildren set
        const string json = """
            {
              "field": { "referenceName": "System.AreaPath" },
              "defaultValue": "Proj",
              "values": [
                { "value": "Proj", "includeChildren": true },
                { "value": "Proj\\Sub", "includeChildren": false }
              ]
            }
            """;
        var package = CreateReadOnlyPackage(json);
        var target = new SimulatedTeamTarget();

        var extension = new TeamAreaPathsTeamExtension(
            TestConnectorCapabilities.All,
            new Mock<ITeamSource>(MockBehavior.Loose).Object,
            target);

        // Act — no translation tool wired: paths pass through unchanged
        await extension.ImportAsync(CreateContext(package.Object, targetEntityId: "target-1"), CancellationToken.None);

        // Assert
        Assert.IsTrue(target.AreaPaths.ContainsKey("target-1"), "SetAreaPathsAsync should have been called");
        var applied = target.AreaPaths["target-1"];
        Assert.AreEqual("Proj", applied.DefaultValue);
        Assert.AreEqual(2, applied.Values.Count);
        Assert.IsFalse(applied.Values.Single(v => v.Value == "Proj\\Sub").IncludeChildren,
            "'Exclude sub areas' must be replayed verbatim on import");
        Assert.IsTrue(applied.Values.Single(v => v.Value == "Proj").IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_AcceptsLegacyBareStringShape_DefaultingIncludeChildrenTrue()
    {
        // Arrange — legacy area-paths.json written by pre-API-shaped exports
        const string json = """
            {
              "defaultAreaPath": "Proj",
              "includedAreaPaths": [ "Proj", "Proj\\Sub" ]
            }
            """;
        var package = CreateReadOnlyPackage(json);
        var target = new SimulatedTeamTarget();

        var extension = new TeamAreaPathsTeamExtension(
            TestConnectorCapabilities.All,
            new Mock<ITeamSource>(MockBehavior.Loose).Object,
            target);

        // Act
        await extension.ImportAsync(CreateContext(package.Object, targetEntityId: "target-1"), CancellationToken.None);

        // Assert — legacy entries import with includeChildren=true (historical behaviour)
        Assert.IsTrue(target.AreaPaths.ContainsKey("target-1"));
        var applied = target.AreaPaths["target-1"];
        Assert.AreEqual("Proj", applied.DefaultValue);
        Assert.AreEqual(2, applied.Values.Count);
        Assert.IsTrue(applied.Values.All(v => v.IncludeChildren));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_DoesNotAreaPathTranslate_CustomTeamFieldValues()
    {
        // Arrange — a custom team field (on-prem): its values are not area paths, so the
        // NodeTranslation area-path map must not rewrite them.
        const string json = """
            {
              "field": { "referenceName": "Custom.Team" },
              "defaultValue": "Alpha",
              "values": [ { "value": "Alpha", "includeChildren": true } ]
            }
            """;
        var package = CreateReadOnlyPackage(json);
        var target = new SimulatedTeamTarget();

        var translationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        translationTool.Setup(t => t.IsEnabled).Returns(true);
        // Strict mock: any TranslatePath call would throw and fail the import silently —
        // asserted below via the verbatim value reaching the target.

        var extension = new TeamAreaPathsTeamExtension(
            TestConnectorCapabilities.All,
            new Mock<ITeamSource>(MockBehavior.Loose).Object,
            target,
            translationTool.Object);

        // Act
        await extension.ImportAsync(CreateContext(package.Object, targetEntityId: "target-1"), CancellationToken.None);

        // Assert — values pass through untranslated and the field reference is carried
        Assert.IsTrue(target.AreaPaths.ContainsKey("target-1"), "custom team field values must still be applied");
        var applied = target.AreaPaths["target-1"];
        Assert.AreEqual("Alpha", applied.DefaultValue);
        Assert.AreEqual("Custom.Team", applied.FieldReferenceName);
        translationTool.Verify(
            t => t.TranslatePath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectMapping>()),
            Times.Never,
            "custom team field values must not be pushed through the area-path map");
    }
}
