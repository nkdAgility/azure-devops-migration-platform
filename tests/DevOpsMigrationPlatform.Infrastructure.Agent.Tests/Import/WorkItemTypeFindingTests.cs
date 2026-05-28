// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
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
            12,
            WorkItemTypeFindingStatus.UnsupportedOnTarget,
            "Type missing on target.");

        Assert.AreEqual("Bug", finding.TypeName);
        Assert.AreEqual(12, finding.Count);
        Assert.AreEqual(WorkItemTypeFindingStatus.UnsupportedOnTarget, finding.Status);
        Assert.AreEqual("Type missing on target.", finding.ErrorMessage);
    }
}
