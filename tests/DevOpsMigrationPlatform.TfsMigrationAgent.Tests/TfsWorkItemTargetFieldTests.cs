// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// PR #222 review (Martin: source work items are replicated to the target): the TFS target writes
/// the source state, as the Azure DevOps target does; it leaves out only fields TFS computes.
/// </summary>
[TestClass]
public class TfsWorkItemTargetFieldTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsWritten_State_IsTrue()
    {
        Assert.IsTrue(TfsWorkItemTarget.IsWritten("System.State"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    [DataRow("System.TeamProject")]
    [DataRow("System.RevisedDate")]
    public void IsWritten_ComputedField_IsFalse(string referenceName)
    {
        Assert.IsFalse(TfsWorkItemTarget.IsWritten(referenceName));
    }
}
