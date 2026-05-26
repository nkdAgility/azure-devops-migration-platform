// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[TestClass]
public sealed class ProjectLifecycleNameGeneratorTests
{
    [TestMethod]
    public void Generate_CreatesCollisionResistantNamesAcrossParallelRuns()
    {
        var sut = new ProjectLifecycleNameGenerator();
        var one = sut.Generate("run-123", "Simulated", "ephemeral");
        var two = sut.Generate("run-123", "Simulated", "ephemeral");

        Assert.AreNotEqual(one, two);
        Assert.IsTrue(one.StartsWith("ephemeral-simulated-run-123-", StringComparison.Ordinal));
        Assert.IsTrue(two.StartsWith("ephemeral-simulated-run-123-", StringComparison.Ordinal));
    }
}
