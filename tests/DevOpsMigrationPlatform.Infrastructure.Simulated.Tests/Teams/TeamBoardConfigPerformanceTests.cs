// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Teams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Teams;

/// <summary>
/// Scale assertion (SC-001): export across 10 simulated teams × 2 boards each
/// must complete within 5 minutes.
/// </summary>
[TestClass]
[TestCategory("SystemTest")]
[TestCategory("SystemTest_Simulated")]
public sealed class TeamBoardConfigPerformanceTests
{
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    [Timeout(300_000)] // 5 minutes
    public async Task Export_TenTeamsWithTwoBoards_CompletesWithinFiveMinutes()
    {
        const int teamCount = 10;

        var cap = new Mock<IConnectorCapabilityProvider>(MockBehavior.Loose);
        cap.Setup(c => c.Has(It.IsAny<Cap>())).Returns(true);

        var options = new BoardConfigExtensionOptions
        {
            Columns = true,
            SwimLanes = true,
            CardRules = true,
            Backlogs = true,
            TaskboardColumns = true,
        };

        var sw = Stopwatch.StartNew();

        for (var i = 0; i < teamCount; i++)
        {
            var slugI = $"team-{i}";
            var written = new List<(string Path, string Json)>();

            var package = new Mock<IPackageAccess>(MockBehavior.Loose);
            package
                .Setup(p => p.PersistContentAsync(
                    It.IsAny<PackageContentContext>(),
                    It.IsAny<PackagePayload>(),
                    It.IsAny<CancellationToken>()))
                .Callback<PackageContentContext, PackagePayload, CancellationToken>(async (ctx, payload, _) =>
                {
                    using var reader = new StreamReader(payload.Content, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync();
                    written.Add((ctx.Address?.RelativePath ?? string.Empty, json));
                })
                .Returns(ValueTask.CompletedTask);

            var ctx = new TeamExtensionContext
            {
                Organisation = "org",
                ProjectName = "Proj",
                EntityId = $"team-id-{i}",
                TargetEntityId = null,
                Package = package.Object,
                Team = new TeamDefinition($"team-id-{i}", $"Team {i}", string.Empty, true),
                Slug = slugI,
                SourceProjectName = "Proj",
            };

            var ext = new BoardConfigTeamExtension(
                Options.Create(options),
                new SimulatedBoardAdapter(),
                cap.Object,
                metrics: null,
                logger: NullLogger<BoardConfigTeamExtension>.Instance);

            await ext.ExportAsync(ctx, CancellationToken.None);

            Assert.AreEqual(1, written.Count, $"Team {i}: one artefact written");
        }

        sw.Stop();

        Assert.IsTrue(
            sw.Elapsed < TimeSpan.FromMinutes(5),
            $"SC-001: 10 team exports completed in {sw.Elapsed.TotalSeconds:F1}s (must be < 300s)");
    }
}
