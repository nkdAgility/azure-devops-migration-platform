// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class NodeReadinessFindingTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_SetsExpectedFieldValues()
    {
        var finding = new NodeReadinessFinding(
            @"Project\Team A",
            NodeReadinessNodeType.Area,
            NodeReadinessFindingStatus.Missing,
            @"TargetProject\Team A");

        Assert.AreEqual(@"Project\Team A", finding.Path);
        Assert.AreEqual(NodeReadinessNodeType.Area, finding.NodeType);
        Assert.AreEqual(NodeReadinessFindingStatus.Missing, finding.Status);
        Assert.AreEqual(@"TargetProject\Team A", finding.TargetPath);
    }
}
