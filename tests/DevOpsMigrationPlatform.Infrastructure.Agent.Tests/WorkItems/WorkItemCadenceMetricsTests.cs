// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemCadenceMetricsTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ReplayCoverageRatio_ForSingleReplayedItemFromHundred_RemainsHigh()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(100, 1);
        Assert.IsTrue(ratio >= 0.99d, $"Expected coverage >= 0.99 but was {ratio:0.000}.");
    }
}
