// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Regression guard for the source-only net481 TFS agent: <see cref="TeamMigrationOrchestrator"/>
/// must activate when only the export-side seams are registered (no <see cref="ITeamTarget"/>).
/// Before the connector deps were made optional in the constructor, Microsoft DI treated the
/// nullable-annotated-but-defaultless parameters as required and threw
/// <c>InvalidOperationException: Unable to resolve service for type 'ITeamTarget'</c>.
/// </summary>
[TestClass]
public sealed class TeamMigrationOrchestratorDiTests
{
    private static ISourceEndpointInfo CreateSourceEndpointInfo()
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://tfs.example/DefaultCollection");
        mock.SetupGet(x => x.Project).Returns("TestProject");
        mock.SetupGet(x => x.ConnectorType).Returns("TeamFoundationServer");
        return mock.Object;
    }

    private static ITargetEndpointInfo CreateTargetEndpointInfo()
    {
        var mock = new Mock<ITargetEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/target");
        mock.SetupGet(x => x.Project).Returns("TargetProject");
        mock.SetupGet(x => x.ConnectorType).Returns("AzureDevOpsServices");
        return mock.Object;
    }

    [TestMethod]
    public void Resolves_On_SourceOnly_Agent_Without_TeamTarget()
    {
        // Mirror the net481 TFS agent registrations: source seams + both endpoint infos,
        // but NOT ITeamTarget (import is guarded out at the module level on that agent).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<ITeamSource>());
        services.AddSingleton(CreateSourceEndpointInfo());
        services.AddSingleton(CreateTargetEndpointInfo());
        services.AddTransient<TeamMigrationOrchestrator>();

        using var provider = services.BuildServiceProvider();

        // BEFORE the fix this throws InvalidOperationException resolving ITeamTarget (RED).
        var orchestrator = provider.GetRequiredService<TeamMigrationOrchestrator>();

        Assert.IsNotNull(orchestrator);
    }
}
