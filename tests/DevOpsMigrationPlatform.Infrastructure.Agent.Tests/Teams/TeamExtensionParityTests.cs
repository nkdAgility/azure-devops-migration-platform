// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// Behaviour-parity tests for each converted team extension (T099).
/// Each test proves that ExportAsync writes the expected artifact to the package.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("DomainTests")]
public class TeamExtensionParityTests
{
    private static TeamExtensionContext BuildContext(Mock<IPackageAccess> package, string slug = "alpha-team")
        => new()
        {
            Organisation = "org",
            ProjectName = "Proj",
            EntityId = "team-1",
            TargetEntityId = null,
            Package = package.Object,
            Team = new TeamDefinition("team-1", "Alpha Team", string.Empty, true),
            Slug = slug,
            SourceProjectName = "Proj",
        };

    private static (Mock<IPackageAccess> package, List<string> writtenPaths) CreateTrackingPackage()
    {
        var writtenPaths = new List<string>();
        var package = new Mock<IPackageAccess>(MockBehavior.Loose);
        package.Setup(p => p.PersistContentAsync(
                It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((ctx, _, _2) =>
                writtenPaths.Add(ctx.Address?.RelativePath ?? string.Empty))
            .Returns(ValueTask.CompletedTask);
        return (package, writtenPaths);
    }

    private static async IAsyncEnumerable<T> YieldOne<T>(T item, [EnumeratorCancellation] CancellationToken _ = default)
    {
        yield return item;
        await Task.CompletedTask;
    }

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task TeamSettingsExtension_WhenSettingsReturned_WritesSettingsJson()
    {
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamSettingsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamSettings(
                BacklogNavigationLevel: "Iteration",
                BugsBehavior: true,
                WorkingDays: new[] { "monday" }));

        var (package, writtenPaths) = CreateTrackingPackage();
        var extension = new TeamSettingsTeamExtension(
            Options.Create(new TeamSettingsExtensionOptions()),
            teamSource: teamSource.Object,
            teamTarget: null,
            logger: NullLogger<TeamSettingsTeamExtension>.Instance);

        await extension.ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.IsTrue(
            writtenPaths.Exists(p => p.Replace('\\', '/').EndsWith("settings.json")),
            "Expected settings.json to be written.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task TeamIterationsExtension_WhenIterationsReturned_WritesIterationsJson()
    {
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamIterationsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldOne(new TeamIteration(
                Id: "iter-1",
                Path: @"Project\Sprint 1",
                Name: "Sprint 1",
                StartDate: null,
                FinishDate: null,
                IsDefault: false,
                IsBacklog: false)));

        var (package, writtenPaths) = CreateTrackingPackage();
        var extension = new TeamIterationsTeamExtension(
            Options.Create(new TeamIterationsExtensionOptions()),
            teamSource: teamSource.Object,
            teamTarget: null,
            nodeTranslationTool: null,
            referencedPathTracker: null,
            logger: NullLogger<TeamIterationsTeamExtension>.Instance);

        await extension.ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.IsTrue(
            writtenPaths.Exists(p => p.Replace('\\', '/').EndsWith("iterations.json")),
            "Expected iterations.json to be written.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task TeamMembersExtension_WhenMembersReturned_WritesMembersJson()
    {
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamMembersAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldOne(new TeamMember(
                Descriptor: "member-1",
                DisplayName: "Alice",
                UniqueName: "alice@example.com",
                IsAdmin: false)));

        var (package, writtenPaths) = CreateTrackingPackage();
        var extension = new TeamMembersTeamExtension(
            Options.Create(new TeamMembersExtensionOptions()),
            teamSource: teamSource.Object,
            teamTarget: null,
            identityTranslationTool: null,
            logger: NullLogger<TeamMembersTeamExtension>.Instance);

        await extension.ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.IsTrue(
            writtenPaths.Exists(p => p.Replace('\\', '/').EndsWith("members.json")),
            "Expected members.json to be written.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task TeamCapacityExtension_WhenCapacityReturned_WritesCapacityJson()
    {
        var teamSource = new Mock<ITeamSource>(MockBehavior.Loose);
        teamSource.Setup(s => s.GetTeamIterationsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(YieldOne(new TeamIteration(
                Id: "sprint-1",
                Path: @"Project\Sprint 1",
                Name: "Sprint 1",
                StartDate: null,
                FinishDate: null,
                IsDefault: false,
                IsBacklog: false)));
        teamSource.Setup(s => s.GetTeamCapacityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TeamCapacityEntry(
                    MemberDescriptor: "desc-alice",
                    MemberDisplayName: "Alice",
                    Activities: new[] { new ActivityEntry("Development", 6.0) },
                    DaysOff: 0)
            });

        var (package, writtenPaths) = CreateTrackingPackage();

        var iterationsJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new TeamIteration("sprint-1", @"Project\Sprint 1", "Sprint 1", null, null, false, false)
        });
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Address != null && c.Address.RelativePath.EndsWith("iterations.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(iterationsJson))));
        package.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Address == null || !c.Address.RelativePath.EndsWith("iterations.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        var extension = new TeamCapacityTeamExtension(
            Options.Create(new TeamCapacityExtensionOptions()),
            teamSource: teamSource.Object,
            teamTarget: null,
            logger: NullLogger<TeamCapacityTeamExtension>.Instance);

        await extension.ExportAsync(BuildContext(package), CancellationToken.None);

        Assert.IsTrue(
            writtenPaths.Exists(p => p.Replace('\\', '/').EndsWith("capacity.json")),
            "Expected capacity.json to be written.");
    }
}
