// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemCadenceProgressTests
{
    [TestMethod]
    public void ShouldPersist_WhenMaxIntervalElapsed_ReturnsTrue()
    {
        var sut = new ProcessingCadencePolicy();
        var now = DateTimeOffset.UtcNow;
        var shouldPersist = sut.ShouldPersist(now, now.AddMinutes(-2), 0, 50, TimeSpan.FromMinutes(1));
        Assert.IsTrue(shouldPersist);
    }
}
