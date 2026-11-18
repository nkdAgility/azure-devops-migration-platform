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
    // PR #222 review: the package names a non-directional link by its link type
    // (System.LinkTypes.Related, the Azure DevOps convention); a directional one by its end.
    // The TFS target looked up only ends, so every Related link was dropped.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    [DataRow("System.LinkTypes.Related", "Related-Forward-End")]
    [DataRow("System.LinkTypes.Hierarchy-Forward", "Hierarchy-Forward-End")]
    public void ResolveLinkEnd_TypeOrEndName_ResolvesTheEnd(string relationName, string expected)
    {
        string? ForwardEndOfType(string name) => name == "System.LinkTypes.Related" ? "Related-Forward-End" : null;
        string? EndByName(string name) => name == "System.LinkTypes.Hierarchy-Forward" ? "Hierarchy-Forward-End" : null;

        Assert.AreEqual(expected, TfsWorkItemTarget.ResolveLinkEnd(relationName, ForwardEndOfType, EndByName));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolveLinkEnd_UnknownName_IsNull()
    {
        Assert.IsNull(TfsWorkItemTarget.ResolveLinkEnd<string>("Nope", _ => null, _ => null));
    }

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
