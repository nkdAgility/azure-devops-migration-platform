// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
[TestCategory("NET481")]
public class TeamExportOrchestratorContractTests
{
    [TestMethod]
    public void Type_IsAvailable_ForNet481Build()
    {
        Assert.IsNotNull(typeof(TeamExportOrchestrator));
    }
}
