// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Teams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Adob = Microsoft.TeamFoundation.Work.WebApi;
using WorkContext = Microsoft.TeamFoundation.Core.WebApi.Types.TeamContext;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests.Teams;

/// <summary>
/// Contract tests for team area path export/import against the Azure DevOps
/// <c>teamsettings/teamfieldvalues</c> REST contract (api-version 7.1): the per-entry
/// <c>includeChildren</c> flag and <c>field.referenceName</c> must round-trip verbatim —
/// no widening of "Exclude sub areas" entries. Wires the real source/target to a mocked
/// <see cref="WorkHttpClient"/> — no network.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
public sealed class AzureDevOpsTeamAreaPathsTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Mock<WorkHttpClient> BuildWorkClient()
        => new(MockBehavior.Loose, new object[] { new Uri("https://dev.azure.com/testorg"), null! });

    private static Mock<IAzureDevOpsClientFactory> BuildFactory(Mock<WorkHttpClient> workClient)
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Loose);
        factory
            .Setup(f => f.CreateWorkClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workClient.Object);
        return factory;
    }

    private static Mock<ISourceEndpointInfo> BuildSource()
    {
        var src = new Mock<ISourceEndpointInfo>(MockBehavior.Loose);
        src.Setup(s => s.ToOrganisationEndpoint())
           .Returns(new OrganisationEndpoint { ResolvedUrl = "https://dev.azure.com/testorg" });
        return src;
    }

    private static Mock<ITargetEndpointInfo> BuildTarget()
    {
        var tgt = new Mock<ITargetEndpointInfo>(MockBehavior.Loose);
        tgt.Setup(t => t.ToOrganisationEndpoint())
           .Returns(new OrganisationEndpoint { ResolvedUrl = "https://dev.azure.com/testorg-target" });
        return tgt;
    }

    // ---------------------------------------------------------------------------
    // Export — GetTeamAreaPathsAsync maps the REST payload without loss
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetTeamAreaPathsAsync_CapturesIncludeChildrenVerbatim()
    {
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetTeamFieldValuesAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adob.TeamFieldValues
            {
                DefaultValue = "Proj\\Area",
                Field = new Adob.FieldReference { ReferenceName = "System.AreaPath" },
                Values = new List<Adob.TeamFieldValue>
                {
                    new() { Value = "Proj\\Area", IncludeChildren = false },
                    new() { Value = "Proj\\Area\\Sub", IncludeChildren = true },
                },
            });

        var source = new AzureDevOpsTeamSource(
            BuildFactory(workClient).Object,
            NullLogger<AzureDevOpsTeamSource>.Instance,
            BuildSource().Object);

        var result = await source.GetTeamAreaPathsAsync("Proj", "team-1", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("Proj\\Area", result!.DefaultValue);
        Assert.AreEqual("System.AreaPath", result.FieldReferenceName);
        Assert.AreEqual(2, result.Values.Count);
        Assert.AreEqual("Proj\\Area", result.Values[0].Value);
        Assert.IsFalse(result.Values[0].IncludeChildren,
            "'Exclude sub areas' (includeChildren=false) must be captured verbatim on export");
        Assert.AreEqual("Proj\\Area\\Sub", result.Values[1].Value);
        Assert.IsTrue(result.Values[1].IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetTeamAreaPathsAsync_CapturesCustomTeamFieldReferenceName()
    {
        // On-prem servers can be configured with a custom team field instead of System.AreaPath
        var workClient = BuildWorkClient();
        workClient
            .Setup(c => c.GetTeamFieldValuesAsync(It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Adob.TeamFieldValues
            {
                DefaultValue = "Alpha",
                Field = new Adob.FieldReference { ReferenceName = "Custom.Team" },
                Values = new List<Adob.TeamFieldValue> { new() { Value = "Alpha", IncludeChildren = true } },
            });

        var source = new AzureDevOpsTeamSource(
            BuildFactory(workClient).Object,
            NullLogger<AzureDevOpsTeamSource>.Instance,
            BuildSource().Object);

        var result = await source.GetTeamAreaPathsAsync("Proj", "team-1", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("Custom.Team", result!.FieldReferenceName);
    }

    // ---------------------------------------------------------------------------
    // Import — SetAreaPathsAsync replays the model verbatim via the PATCH contract
    // ---------------------------------------------------------------------------

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task SetAreaPathsAsync_ReplaysIncludeChildrenVerbatim()
    {
        var workClient = BuildWorkClient();
        Adob.TeamFieldValuesPatch? captured = null;
        workClient
            .Setup(c => c.UpdateTeamFieldValuesAsync(It.IsAny<Adob.TeamFieldValuesPatch>(), It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<Adob.TeamFieldValuesPatch, WorkContext, object?, CancellationToken>((patch, _, _, _) => captured = patch)
            .ReturnsAsync(new Adob.TeamFieldValues());

        var target = new AzureDevOpsTeamTarget(
            BuildFactory(workClient).Object,
            BuildTarget().Object,
            NullLogger<AzureDevOpsTeamTarget>.Instance);

        await target.SetAreaPathsAsync("Proj", "team-1", new TeamAreaPaths(
            "Proj\\Area",
            new List<TeamFieldValueEntry>
            {
                new("Proj\\Area", IncludeChildren: false),
                new("Proj\\Other", IncludeChildren: true),
            }), CancellationToken.None);

        Assert.IsNotNull(captured, "UpdateTeamFieldValuesAsync should have been called");
        Assert.AreEqual("Proj\\Area", captured!.DefaultValue);
        var values = captured.Values.ToList();
        Assert.AreEqual(2, values.Count, "entries must be replayed verbatim — no duplicates, no widening");
        Assert.IsFalse(values.Single(v => v.Value == "Proj\\Area").IncludeChildren,
            "'Exclude sub areas' must not be widened to includeChildren=true on import");
        Assert.IsTrue(values.Single(v => v.Value == "Proj\\Other").IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task SetAreaPathsAsync_AddsDefaultEntry_WhenValuesOmitDefault()
    {
        // Legacy packages list only non-default entries; the PATCH contract requires the
        // default value to appear in values, so it is added with includeChildren=true.
        var workClient = BuildWorkClient();
        Adob.TeamFieldValuesPatch? captured = null;
        workClient
            .Setup(c => c.UpdateTeamFieldValuesAsync(It.IsAny<Adob.TeamFieldValuesPatch>(), It.IsAny<WorkContext>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<Adob.TeamFieldValuesPatch, WorkContext, object?, CancellationToken>((patch, _, _, _) => captured = patch)
            .ReturnsAsync(new Adob.TeamFieldValues());

        var target = new AzureDevOpsTeamTarget(
            BuildFactory(workClient).Object,
            BuildTarget().Object,
            NullLogger<AzureDevOpsTeamTarget>.Instance);

        await target.SetAreaPathsAsync("Proj", "team-1", new TeamAreaPaths(
            "Proj\\Area",
            new List<TeamFieldValueEntry> { new("Proj\\Sub", IncludeChildren: true) }), CancellationToken.None);

        Assert.IsNotNull(captured);
        var values = captured!.Values.ToList();
        Assert.AreEqual(2, values.Count);
        Assert.IsTrue(values.Single(v => v.Value == "Proj\\Area").IncludeChildren,
            "the synthesised default entry defaults to includeChildren=true");
        Assert.IsTrue(values.Any(v => v.Value == "Proj\\Sub"));
    }
}
