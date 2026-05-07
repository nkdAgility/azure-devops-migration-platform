// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Discovery;

[TestClass]
public sealed class ProcessingCadencePolicyTests
{
    [TestMethod]
    public void ShouldPersist_WhenBatchThresholdReached_ReturnsTrue()
    {
        var sut = new ProcessingCadencePolicy();
        var shouldPersist = sut.ShouldPersist(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 10, TimeSpan.FromMinutes(5));
        Assert.IsTrue(shouldPersist);
    }
}
