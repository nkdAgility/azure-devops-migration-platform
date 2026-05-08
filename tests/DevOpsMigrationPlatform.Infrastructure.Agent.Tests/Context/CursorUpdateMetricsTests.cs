// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class CursorUpdateMetricsTests
{
    [TestMethod]
    public void ReplayCoverageRatio_EnforcesNearLatestThreshold_ForResume()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed: 1_000, replayedAfterResume: 50);
        Assert.IsTrue(ratio >= 0.95d, $"Expected replay coverage >= 0.95 but was {ratio:0.000}.");
    }

    [TestMethod]
    public void ReplayCoverageRatio_FallsBelowThreshold_WhenReplayWindowTooLarge()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed: 1_000, replayedAfterResume: 200);
        Assert.IsTrue(ratio < 0.95d, $"Expected replay coverage < 0.95 but was {ratio:0.000}.");
    }
}
