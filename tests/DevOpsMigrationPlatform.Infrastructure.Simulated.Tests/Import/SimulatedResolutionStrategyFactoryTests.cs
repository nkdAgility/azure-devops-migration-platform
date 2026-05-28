// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Import;

[TestClass]
public sealed class SimulatedResolutionStrategyFactoryTests
{
    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsEmpty_ReturnsNullResolutionStrategy()
    {
        var factory = new SimulatedResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions();

        var strategy = await factory.CreateAsync(
            options,
            new SimulatedWorkItemTarget(),
            new TestTargetEndpointInfo(),
            CancellationToken.None);

        Assert.IsInstanceOfType<NullResolutionStrategy>(strategy);
    }

    [TestMethod]
    public async Task CreateAsync_WhenStrategyIsExplicit_ThrowsInvalidOperationException()
    {
        var factory = new SimulatedResolutionStrategyFactory();
        var options = new WorkItemResolutionStrategyOptions { Strategy = "TargetField", FieldName = "Custom.SourceId" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            factory.CreateAsync(
                options,
                new SimulatedWorkItemTarget(),
                new TestTargetEndpointInfo(),
                CancellationToken.None));
    }

    private sealed class TestTargetEndpointInfo : ITargetEndpointInfo
    {
        public string Url => "https://simulated.dev.azure.com";
        public string Project => "Demo";
        public string ConnectorType => "Simulated";
        public OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
    }
}
