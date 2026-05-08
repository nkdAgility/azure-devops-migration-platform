// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RuntimeStateMetricsTests
{
    [TestMethod]
    public void ReplayCoverageRatio_WhenNoReplay_ReturnsOne()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed: 42, replayedAfterResume: 0);
        Assert.AreEqual(1d, ratio, 0.00001d);
    }

    [TestMethod]
    public void ReplayCoverageRatio_WhenReplayExceedsTotal_ClampsToZero()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed: 10, replayedAfterResume: 99);
        Assert.AreEqual(0d, ratio, 0.00001d);
    }

    [TestMethod]
    [DataRow(100, 4, 0.96d)]
    [DataRow(200, 5, 0.975d)]
    [DataRow(1000, 50, 0.95d)]
    public void ReplayCoverageRatio_ComputesExpectedCoverage(int totalProcessed, int replayedAfterResume, double expected)
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed, replayedAfterResume);
        Assert.AreEqual(expected, ratio, 0.00001d);
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(-1, 10)]
    public void ReplayCoverageRatio_WhenTotalIsNonPositive_ReturnsOne(int totalProcessed, int replayedAfterResume)
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed, replayedAfterResume);
        Assert.AreEqual(1d, ratio, 0.00001d);
    }
}
