// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests.WorkItems;

/// <summary>
/// #169 live E2E: TFS Object Model revisions carry every field, including ones with no value.
/// Azure DevOps rejects an <c>add</c> with a null value ("Value cannot be null"), so the patch
/// must leave valueless fields out, as REST-exported revisions (which never carry them) already do.
/// </summary>
[TestClass]
public class AzureDevOpsWorkItemTargetFieldPatchTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void BuildFieldPatch_FieldWithNoValue_IsLeftOut()
    {
        var patch = AzureDevOpsWorkItemTarget.BuildFieldPatch(
        [
            new WorkItemField { ReferenceName = "System.Title", Value = "E2E169 story" },
            new WorkItemField { ReferenceName = "Microsoft.VSTS.Scheduling.FinishDate", Value = null }
        ], Operation.Add);

        CollectionAssert.AreEqual(new[] { "/fields/System.Title" }, patch.Select(p => p.Path).ToArray());
    }

    // #169 live E2E: TF51541 "The Area/Iteration ID is not recognized: 681". Source node IDs,
    // revision counters and computed counts mean nothing on the target and are read-only there.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    [DataRow("System.Id")]
    [DataRow("System.Rev")]
    [DataRow("System.AreaId")]
    [DataRow("System.IterationId")]
    [DataRow("System.NodeName")]
    [DataRow("System.Watermark")]
    [DataRow("System.AuthorizedAs")]
    [DataRow("System.AuthorizedDate")]
    [DataRow("System.RevisedDate")]
    [DataRow("System.Parent")]
    [DataRow("System.RelatedLinkCount")]
    [DataRow("System.ExternalLinkCount")]
    [DataRow("System.RemoteLinkCount")]
    [DataRow("System.HyperLinkCount")]
    [DataRow("System.AttachedFileCount")]
    [DataRow("System.CommentCount")]
    [DataRow("System.BoardColumn")]
    [DataRow("System.BoardColumnDone")]
    [DataRow("System.BoardLane")]
    public void BuildFieldPatch_ReadOnlyOrComputedSystemField_IsLeftOut(string referenceName)
    {
        var patch = AzureDevOpsWorkItemTarget.BuildFieldPatch(
        [
            new WorkItemField { ReferenceName = "System.AreaPath", Value = @"ProjectA\Team A" },
            new WorkItemField { ReferenceName = referenceName, Value = "681" }
        ], Operation.Add);

        CollectionAssert.AreEqual(new[] { "/fields/System.AreaPath" }, patch.Select(p => p.Path).ToArray());
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void BuildFieldPatch_UpdateClearsAFieldTheSourceCleared()
    {
        // PR #222 review: a revision that clears AssignedTo must clear it on the target. Only
        // fields the target item has set are cleared; TFS revisions carry every field, mostly null.
        var patch = AzureDevOpsWorkItemTarget.BuildFieldPatch(
        [
            new WorkItemField { ReferenceName = "System.AssignedTo", Value = null },
            new WorkItemField { ReferenceName = "Microsoft.VSTS.Scheduling.FinishDate", Value = null }
        ], Operation.Add, fieldsSetOnTarget: new System.Collections.Generic.HashSet<string> { "System.AssignedTo" });

        var op = patch.Single();
        Assert.AreEqual("/fields/System.AssignedTo", op.Path);
        Assert.AreEqual(Operation.Remove, op.Operation);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void BuildFieldPatch_State_IsWritten()
    {
        // PR #222 review: the goal is that source work items are replicated to the target, so a
        // source transition (New -> Closed) must reach it. Mismatched processes map states with
        // the FieldTransform MapValue tool before this point.
        var patch = AzureDevOpsWorkItemTarget.BuildFieldPatch(
            [new WorkItemField { ReferenceName = "System.State", Value = "Closed" }], Operation.Add);

        Assert.AreEqual("/fields/System.State", patch.Single().Path);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void BuildFieldPatch_EmptyStringValue_IsKept()
    {
        var patch = AzureDevOpsWorkItemTarget.BuildFieldPatch(
            [new WorkItemField { ReferenceName = "Microsoft.VSTS.Common.Risk", Value = "" }], Operation.Add);

        Assert.AreEqual("/fields/Microsoft.VSTS.Common.Risk", patch.Single().Path, "An empty string is a real value.");
    }
}
