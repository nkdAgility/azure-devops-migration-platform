// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemCadenceMetricsTests
{
    [TestMethod]
    public void ReplayCoverageRatio_WithReplay_IsLessThanOne()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(100, 1);
        Assert.IsTrue(ratio < 1d);
    }
}
