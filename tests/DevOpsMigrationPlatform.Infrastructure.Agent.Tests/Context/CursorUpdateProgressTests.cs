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
    public void ShouldPersist_WhenBatchAndIntervalNotMet_ReturnsFalse()
    {
        var sut = new ProcessingCadencePolicy();
        var shouldPersist = sut.ShouldPersist(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 50, TimeSpan.FromMinutes(10));
        Assert.IsFalse(shouldPersist);
    }
}
