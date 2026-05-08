// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemBatchResumeCadenceTests
{
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
}
