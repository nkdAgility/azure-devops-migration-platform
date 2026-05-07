// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RuntimeStateProgressTests
{
    [TestMethod]
    public void ShouldPersist_WhenIntervalElapsed_ReturnsTrue()
    {
        var sut = new ProcessingCadencePolicy();
        var result = sut.ShouldPersist(
            nowUtc: new System.DateTimeOffset(2026, 5, 7, 11, 0, 0, System.TimeSpan.Zero),
            lastPersistUtc: new System.DateTimeOffset(2026, 5, 7, 10, 50, 0, System.TimeSpan.Zero),
            processedSincePersist: 1,
            minimumBatchSize: 50,
            maxInterval: System.TimeSpan.FromMinutes(5));

        Assert.IsTrue(result);
    }
}
