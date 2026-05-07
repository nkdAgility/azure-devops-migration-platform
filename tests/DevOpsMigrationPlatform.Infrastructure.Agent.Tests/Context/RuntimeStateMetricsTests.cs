// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RuntimeStateMetricsTests
{
    [TestMethod]
    public void ReplayCoverageRatio_IsDeterministic()
    {
        var first = ProcessingCadencePolicy.ReplayCoverageRatio(100, 4);
        var second = ProcessingCadencePolicy.ReplayCoverageRatio(100, 4);
        Assert.AreEqual(first, second);
    }
}
