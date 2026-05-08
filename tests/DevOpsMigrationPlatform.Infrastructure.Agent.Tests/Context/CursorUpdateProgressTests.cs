// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class CursorUpdateProgressTests
{
    [TestMethod]
    public void ShouldPersist_WhenElapsedIntervalExactlyMatchesThreshold()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var shouldPersist = sut.ShouldPersist(now, now.AddMinutes(-5), processedSincePersist: 0, minimumBatchSize: 50, maxInterval: TimeSpan.FromMinutes(5));

        Assert.IsTrue(shouldPersist);
    }

    [TestMethod]
    public void ShouldNotPersist_WhenBatchAndIntervalThresholdsNotMet()
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);
        var shouldPersist = sut.ShouldPersist(now, now.AddMinutes(-4), processedSincePersist: 49, minimumBatchSize: 50, maxInterval: TimeSpan.FromMinutes(5));

        Assert.IsFalse(shouldPersist);
    }
}
