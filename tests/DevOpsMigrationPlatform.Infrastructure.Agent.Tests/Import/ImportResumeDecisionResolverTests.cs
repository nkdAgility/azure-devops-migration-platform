// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

/// <summary>
/// Characterization tests for <see cref="ImportResumeDecisionResolver"/>.
/// These tests pin the exact resume semantics and must stay green through Stage 2
/// refactoring — they are the frozen invariant for the cursor pipeline generalisation.
/// </summary>
[TestClass]
public class ImportResumeDecisionResolverTests
{
    private const string Folder = "WorkItems/2024-01-01/00000638000000000001-1-0";

    // ── Null / empty cursor ───────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_NullCursor_StartsFromBeginning()
    {
        var decision = ImportResumeDecisionResolver.Resolve(Folder, null);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.IsNull(decision.ResumeAtStage);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_CursorWithEmptyLastProcessed_StartsFromBeginning()
    {
        var cursor = new CursorEntry { LastProcessed = string.Empty, Stage = CursorStage.Completed };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.IsNull(decision.ResumeAtStage);
    }

    // ── Folder ordering relative to LastProcessed ────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_FolderBeforeLastProcessed_Skips()
    {
        // "00000638000000000001-1-0" sorts before "00000638000000000002-1-0"
        const string laterFolder = "WorkItems/2024-01-01/00000638000000000002-1-0";
        var cursor = new CursorEntry { LastProcessed = laterFolder, Stage = CursorStage.Completed };

        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsTrue(decision.ShouldSkip);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_FolderAfterLastProcessed_StartsFromBeginning()
    {
        const string earlierFolder = "WorkItems/2024-01-01/00000638000000000000-1-0";
        var cursor = new CursorEntry { LastProcessed = earlierFolder, Stage = CursorStage.Completed };

        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.IsNull(decision.ResumeAtStage);
    }

    // ── Exact LastProcessed folder, Stage = Completed ────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_StageCompleted_Skips()
    {
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.Completed };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsTrue(decision.ShouldSkip);
    }

    // ── Exact LastProcessed folder, mid-pipeline stages ──────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_StageCreatedOrUpdated_ResumesAtAppliedFields()
    {
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.CreatedOrUpdated };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.AreEqual(CursorStage.AppliedFields, decision.ResumeAtStage);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_StageAppliedFields_ResumesAtAppliedLinks()
    {
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.AppliedFields };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.AreEqual(CursorStage.AppliedLinks, decision.ResumeAtStage);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_StageAppliedLinks_ResumesAtUploadedAttachments()
    {
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.AppliedLinks };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.AreEqual(CursorStage.UploadedAttachments, decision.ResumeAtStage);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_StageUploadedAttachments_ResumesAtAppliedComments()
    {
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.UploadedAttachments };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsFalse(decision.ShouldSkip);
        Assert.AreEqual(CursorStage.AppliedComments, decision.ResumeAtStage);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_StageAppliedComments_Skips()
    {
        // AppliedComments → next is Completed → treated as done (Skip)
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.AppliedComments };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsTrue(decision.ShouldSkip);
    }

    // ── Unknown stage → exception ─────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_ExactFolder_UnknownStage_Throws()
    {
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = "UnknownStage" };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImportResumeDecisionResolver.Resolve(Folder, cursor));
    }

    // ── Path normalisation ────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_BackslashPath_NormalisedToForwardSlash()
    {
        var backslashFolder = Folder.Replace('/', '\\');
        var cursor = new CursorEntry { LastProcessed = Folder, Stage = CursorStage.Completed };
        var decision = ImportResumeDecisionResolver.Resolve(backslashFolder, cursor);
        Assert.IsTrue(decision.ShouldSkip);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Resolve_TrailingSlash_NormalisedBeforeComparison()
    {
        var cursor = new CursorEntry { LastProcessed = Folder + "/", Stage = CursorStage.Completed };
        var decision = ImportResumeDecisionResolver.Resolve(Folder, cursor);
        Assert.IsTrue(decision.ShouldSkip);
    }
}
