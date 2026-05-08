// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RuntimeStateProgressTests
{
    [TestMethod]
    public void ShouldPersist_WhenProcessedSincePersist_ReachesBatchThreshold()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var lastPersist = now.AddSeconds(-30);

        var shouldPersist = sut.ShouldPersist(now, lastPersist, processedSincePersist: 50, minimumBatchSize: 50, maxInterval: TimeSpan.FromMinutes(5));

        Assert.IsTrue(shouldPersist);
    }

    [TestMethod]
    public void ShouldPersist_WhenMaximumIntervalElapsed()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var lastPersist = now.AddMinutes(-6);

        var shouldPersist = sut.ShouldPersist(now, lastPersist, processedSincePersist: 1, minimumBatchSize: 50, maxInterval: TimeSpan.FromMinutes(5));

        Assert.IsTrue(shouldPersist);
    }

    [TestMethod]
    public void ShouldNotPersist_WhenBatchAndIntervalThresholdsNotMet()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var lastPersist = now.AddMinutes(-1);

        var shouldPersist = sut.ShouldPersist(now, lastPersist, processedSincePersist: 49, minimumBatchSize: 50, maxInterval: TimeSpan.FromMinutes(5));

        Assert.IsFalse(shouldPersist);
    }
}
