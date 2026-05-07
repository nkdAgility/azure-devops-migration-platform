// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class CursorUpdateMetricsTests
{
    [TestMethod]
    public void ReplayCoverageRatio_WhenNoReplay_IsOne()
    {
        Assert.AreEqual(1d, ProcessingCadencePolicy.ReplayCoverageRatio(42, 0), 0.00001d);
    }
}
