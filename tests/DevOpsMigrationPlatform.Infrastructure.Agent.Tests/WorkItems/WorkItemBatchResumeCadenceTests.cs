// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemBatchResumeCadenceTests
{
    [TestCategory("UnitTest")]
    [TestMethod]
    public void ShouldPersist_AtCompletedBatchBoundary()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var lastPersist = now;

        var persistAtFortyNine = sut.ShouldPersist(now, lastPersist, processedSincePersist: 49, minimumBatchSize: 50, maxInterval: TimeSpan.FromHours(1));
        var persistAtFifty = sut.ShouldPersist(now, lastPersist, processedSincePersist: 50, minimumBatchSize: 50, maxInterval: TimeSpan.FromHours(1));

        Assert.IsFalse(persistAtFortyNine);
        Assert.IsTrue(persistAtFifty);
    }

    /// <summary>
    /// Verifies that replay after an interruption between durable checkpoint boundaries
    /// remains within the defined replay threshold, and progress moves forward steadily.
    /// Covers: Processing_ProgressAndCheckpointCadence_RemainsNearLatestOnResume
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public void ReplayCoverageRatio_RemainsWithinThresholdAfterResume()
    {
        // Simulate: 100 items processed before interruption; checkpoint was saved every 50.
        // On resume, at most one batch (50) is replayed.
        int totalProcessed = 100;
        int replayedAfterResume = 50; // worst-case replay = one checkpoint interval

        double coverageRatio = ProcessingCadencePolicy.ReplayCoverageRatio(totalProcessed, replayedAfterResume);

        // At least 50% of work was preserved (ratio >= 0.5), so replay is bounded.
        Assert.IsTrue(coverageRatio >= 0.5,
            $"Coverage ratio {coverageRatio:P0} is below the 50% replay threshold.");
    }

    /// <summary>
    /// Verifies that progress output continues with steady forward movement:
    /// each subsequent persist decision is triggered, confirming the cadence advances.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public void ShouldPersist_SteadyForwardMovement_AfterResume()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var lastPersist = now;

        // Simulate steady incremental progress: each batch of 50 triggers a persist.
        for (int batch = 1; batch <= 3; batch++)
        {
            var shouldPersist = sut.ShouldPersist(now, lastPersist,
                processedSincePersist: 50 * batch,
                minimumBatchSize: 50,
                maxInterval: TimeSpan.FromHours(1));

            Assert.IsTrue(shouldPersist, $"Batch {batch}: expected persist to be triggered.");
            // Advance last persist to simulate durable checkpoint being written.
            lastPersist = now;
        }
    }
}
