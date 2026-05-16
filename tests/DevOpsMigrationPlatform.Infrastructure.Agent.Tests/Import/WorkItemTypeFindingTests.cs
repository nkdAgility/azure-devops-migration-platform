// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class WorkItemTypeFindingTests
{
    [TestMethod]
    public void Constructor_SetsExpectedFieldValues()
    {
        var finding = new WorkItemTypeFinding(
            "Bug",
            WorkItemTypeFindingStatus.Missing,
            "Bug");

        Assert.AreEqual("Bug", finding.TypeName);
        Assert.AreEqual(WorkItemTypeFindingStatus.Missing, finding.Status);
        Assert.AreEqual("Bug", finding.TargetReference);
    }
}
