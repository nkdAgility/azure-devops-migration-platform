// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
[TestCategory("NET481")]
public sealed class TeamsNet481ContractTests
{
    [TestMethod]
    public void ITeamsOrchestrator_IsAvailable_OnNet481_WithSharedMembersOnly()
    {
        var contractType = Type.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.Modules.ITeamsOrchestrator, DevOpsMigrationPlatform.Abstractions.Agent",
            throwOnError: false);

        Assert.IsNotNull(contractType, "ITeamsOrchestrator should be available on net481 for shared export/validate orchestration.");

        var methodNames = contractType!.GetMethods().Select(m => m.Name).OrderBy(name => name).ToArray();

        CollectionAssert.Contains(methodNames, nameof(IIdentitiesOrchestrator.ExportAsync));
        CollectionAssert.Contains(methodNames, nameof(IIdentitiesOrchestrator.ValidateAsync));
        CollectionAssert.DoesNotContain(methodNames, "ImportAsync");
    }

    [TestMethod]
    public void AddTfsMigrationAgentServices_RegistersTeamsModule_ForNet481ExportPipeline()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddTfsMigrationAgentServices(configuration, new Uri("http://localhost:5100"));

        var teamsModuleDescriptor = services
            .Where(d => d.ServiceType == typeof(IModule))
            .FirstOrDefault(d => string.Equals(d.ImplementationType?.Name, "TeamsModule", StringComparison.Ordinal));

        Assert.IsNotNull(teamsModuleDescriptor, "TeamsModule should be registered in the TFS agent export pipeline.");
    }
}
