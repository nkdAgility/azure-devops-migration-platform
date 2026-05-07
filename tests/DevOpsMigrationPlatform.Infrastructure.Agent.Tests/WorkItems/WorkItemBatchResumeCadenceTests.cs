// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemBatchResumeCadenceTests
{
    [TestMethod]
    public void ReplayCoverageRatio_WithSmallReplayWindow_RemainsHigh()
    {
        var ratio = ProcessingCadencePolicy.ReplayCoverageRatio(200, 5);
        Assert.IsTrue(ratio >= 0.97d);
    }
}
