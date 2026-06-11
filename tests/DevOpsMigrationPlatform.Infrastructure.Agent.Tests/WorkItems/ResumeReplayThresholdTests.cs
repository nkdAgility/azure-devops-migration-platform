// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class ResumeReplayThresholdTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ReplayCoverageRatio_IsAtLeast95Percent_ForNearLatestResume()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed: 1_000, replayedAfterResume: 50);
        Assert.IsTrue(ratio >= 0.95d, $"Expected coverage >= 0.95 but was {ratio:0.000}.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ReplayCoverageRatio_DropsBelow95Percent_WhenReplayExceedsAllowedWindow()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed: 1_000, replayedAfterResume: 51);
        Assert.IsTrue(ratio < 0.95d, $"Expected coverage < 0.95 but was {ratio:0.000}.");
    }
}
