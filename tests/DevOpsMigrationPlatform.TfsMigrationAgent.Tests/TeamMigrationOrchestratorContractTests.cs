// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
public class TeamMigrationOrchestratorContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Type_IsAvailable_ForNet481Build()
    {
        Assert.IsNotNull(typeof(TeamMigrationOrchestrator));
    }
}
