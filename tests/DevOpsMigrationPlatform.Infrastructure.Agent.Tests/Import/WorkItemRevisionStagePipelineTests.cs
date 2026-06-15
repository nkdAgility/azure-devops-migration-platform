// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

/// <summary>
/// Verifies the stage-list source of truth in <see cref="WorkItemRevisionStagePipeline"/>.
/// These are the RED tests for Increment 2.2 — they fail to compile until
/// <see cref="WorkItemRevisionStagePipeline"/> is created.
/// </summary>
[TestClass]
public class WorkItemRevisionStagePipelineTests
{
    // ── StageNames order ─────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void StageNames_ContainsFourStagesInExecutionOrder()
    {
        Assert.AreEqual(4, WorkItemRevisionStagePipeline.StageNames.Count);
        Assert.AreEqual(CursorStage.CreatedOrUpdated,    WorkItemRevisionStagePipeline.StageNames[0]);
        Assert.AreEqual(CursorStage.AppliedFields,       WorkItemRevisionStagePipeline.StageNames[1]);
        Assert.AreEqual(CursorStage.AppliedLinks,        WorkItemRevisionStagePipeline.StageNames[2]);
        Assert.AreEqual(CursorStage.UploadedAttachments, WorkItemRevisionStagePipeline.StageNames[3]);
    }

    // ── GetNextStage mirrors the resolver's original switch ──────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetNextStage_CreatedOrUpdated_ReturnsAppliedFields()
        => Assert.AreEqual(CursorStage.AppliedFields,
            WorkItemRevisionStagePipeline.GetNextStage(CursorStage.CreatedOrUpdated));

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetNextStage_AppliedFields_ReturnsAppliedLinks()
        => Assert.AreEqual(CursorStage.AppliedLinks,
            WorkItemRevisionStagePipeline.GetNextStage(CursorStage.AppliedFields));

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetNextStage_AppliedLinks_ReturnsUploadedAttachments()
        => Assert.AreEqual(CursorStage.UploadedAttachments,
            WorkItemRevisionStagePipeline.GetNextStage(CursorStage.AppliedLinks));

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetNextStage_UploadedAttachments_ReturnsCompleted()
        => Assert.AreEqual(CursorStage.Completed,
            WorkItemRevisionStagePipeline.GetNextStage(CursorStage.UploadedAttachments));

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetNextStage_Completed_ReturnsNull()
        => Assert.IsNull(WorkItemRevisionStagePipeline.GetNextStage(CursorStage.Completed));

    // ── ShouldRunStage (position-based) ─────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ShouldRunStage_NullResumeAt_AlwaysTrue()
    {
        foreach (var stage in WorkItemRevisionStagePipeline.StageNames)
            Assert.IsTrue(WorkItemRevisionStagePipeline.ShouldRunStage(stage, null),
                $"Expected true for stage={stage} with null resumeAt");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ShouldRunStage_ResumeAtAppliedLinks_SkipsCreatedOrUpdatedAndAppliedFields()
    {
        Assert.IsFalse(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.CreatedOrUpdated, CursorStage.AppliedLinks));
        Assert.IsFalse(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.AppliedFields, CursorStage.AppliedLinks));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ShouldRunStage_ResumeAtAppliedLinks_RunsAppliedLinksAndLater()
    {
        Assert.IsTrue(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.AppliedLinks, CursorStage.AppliedLinks));
        Assert.IsTrue(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.UploadedAttachments, CursorStage.AppliedLinks));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ShouldRunStage_ResumeAtAppliedFields_SkipsOnlyCreatedOrUpdated()
    {
        Assert.IsFalse(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.CreatedOrUpdated, CursorStage.AppliedFields));
        Assert.IsTrue(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.AppliedFields, CursorStage.AppliedFields));
        Assert.IsTrue(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.AppliedLinks, CursorStage.AppliedFields));
        Assert.IsTrue(WorkItemRevisionStagePipeline.ShouldRunStage(
            CursorStage.UploadedAttachments, CursorStage.AppliedFields));
    }
}
