// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemCadenceProgressTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    [DataRow(50, 1, 10, true)]
    [DataRow(1, 11, 10, true)]
    [DataRow(49, 1, 10, false)]
    public void ShouldPersist_UsesBatchOrIntervalThreshold(int processedSincePersist, int elapsedMinutes, int maxIntervalMinutes, bool expected)
    {
        var sut = new ProcessingCadencePolicy();
        var now = new DateTimeOffset(2026, 5, 7, 11, 0, 0, TimeSpan.Zero);

        var shouldPersist = sut.ShouldPersist(
            nowUtc: now,
            lastPersistUtc: now.AddMinutes(-elapsedMinutes),
            processedSincePersist: processedSincePersist,
            minimumBatchSize: 50,
            maxInterval: TimeSpan.FromMinutes(maxIntervalMinutes));

        Assert.AreEqual(expected, shouldPersist);
    }
}
