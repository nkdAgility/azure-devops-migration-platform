// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// Production DI wiring tests for <see cref="TeamsServiceCollectionExtensions.AddTeamsModule"/>:
/// the service collection must yield all five Teams <see cref="IModuleExtension"/>
/// implementations (iterations, members, capacity, area paths, board config) — matching
/// what <c>TeamsOrchestrator</c> dispatches — including on source-only hosts (the net481
/// TFS agent shape) where no connector registers an <see cref="ITeamTarget"/>.
/// </summary>
[TestClass]
public sealed class TeamsModuleRegistrationTests
{
    private static readonly string[] s_expectedTeamsExtensions =
    [
        "TeamIterations", "TeamMembers", "TeamCapacity", "TeamAreaPaths", "BoardConfig"
    ];

    /// <summary>
    /// Registers the seams every real host provides before <c>AddTeamsModule</c> runs:
    /// configuration, connector capabilities, team source, board adapter, and target
    /// endpoint info. <paramref name="connectorRegistersTeamTarget"/> distinguishes the
    /// full ADO/Simulated host shape from the source-only TFS host shape.
    /// </summary>
    private static ServiceCollection CreateHostShapedServices(bool connectorRegistersTeamTarget)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectorCapabilityProvider>(TestConnectorCapabilities.All);
        services.AddSingleton(Mock.Of<ITeamSource>());
        services.AddSingleton(Mock.Of<ITeamBoardAdapter>());
        services.AddSingleton(Mock.Of<ITargetEndpointInfo>());
        if (connectorRegistersTeamTarget)
            services.AddSingleton(Mock.Of<ITeamTarget>());
        return services;
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddTeamsModule_RegistersAllFiveTeamsExtensions()
    {
        // Arrange — full host shape (a connector registered ITeamTarget)
        var services = CreateHostShapedServices(connectorRegistersTeamTarget: true);
        services.AddTeamsModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act — resolve what TeamsOrchestrator receives from DI
        var extensions = scope.ServiceProvider.GetServices<IModuleExtension>()
            .Where(e => e.Module == "Teams")
            .ToList();

        // Assert — every Teams capability sub-concern is dispatched in production
        var names = extensions.Select(e => e.Name).ToList();
        foreach (var expected in s_expectedTeamsExtensions)
        {
            Assert.IsTrue(names.Contains(expected),
                $"AddTeamsModule must register the '{expected}' extension as IModuleExtension " +
                $"(got: [{string.Join(", ", names)}]). Without it, that Teams sub-concern " +
                "silently never runs in real migrations.");
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddTeamsModule_ResolvesExtensions_OnSourceOnlyHost()
    {
        // Arrange — source-only host shape (net481 TFS agent): no connector registers
        // ITeamTarget. Export extensions must still resolve; ITeamTarget falls back to
        // the CompositeTeamTarget dispatcher, which only resolves a concrete target when
        // an import method is invoked (never on net481 — import dispatch is compiled out).
        var services = CreateHostShapedServices(connectorRegistersTeamTarget: false);
        services.AddTeamsModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var extensions = scope.ServiceProvider.GetServices<IModuleExtension>()
            .Where(e => e.Module == "Teams")
            .ToList();

        // Assert — resolution succeeds and all five extensions are present
        var names = extensions.Select(e => e.Name).ToList();
        foreach (var expected in s_expectedTeamsExtensions)
        {
            Assert.IsTrue(names.Contains(expected),
                $"Source-only hosts must still resolve the '{expected}' extension " +
                $"(got: [{string.Join(", ", names)}]).");
        }

        Assert.IsInstanceOfType<CompositeTeamTarget>(
            scope.ServiceProvider.GetService<ITeamTarget>(),
            "with no connector-registered ITeamTarget, the composite dispatcher must back the seam");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddTeamsModule_DoesNotOverrideConnectorRegisteredTeamTarget()
    {
        // Arrange — a connector already registered a concrete ITeamTarget
        var services = CreateHostShapedServices(connectorRegistersTeamTarget: true);
        services.AddTeamsModule();

        using var provider = services.BuildServiceProvider();

        // Assert — the module's fallback must not displace the connector's registration
        Assert.IsNotInstanceOfType<CompositeTeamTarget>(
            provider.GetRequiredService<ITeamTarget>(),
            "AddTeamsModule must TryAdd the composite fallback, never override a connector's ITeamTarget");
    }
}
