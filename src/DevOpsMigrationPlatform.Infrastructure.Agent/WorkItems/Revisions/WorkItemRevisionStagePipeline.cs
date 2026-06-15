// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

/// <summary>
/// Single source of truth for the ordered per-revision import stage sequence.
/// Both <c>ImportResumeDecisionResolver</c> and <c>WorkItemResolutionProcessor</c>
/// derive stage ordering from this list — no hard-coded switches or alphabetical
/// string comparisons elsewhere.
/// </summary>
internal static class WorkItemRevisionStagePipeline
{
    /// <summary>
    /// Ordered cursor-bearing stage names. <c>Completed</c> is NOT included here
    /// — it is written unconditionally after all stages, never skipped.
    /// </summary>
    internal static readonly IReadOnlyList<string> StageNames =
    [
        CursorStage.CreatedOrUpdated,
        CursorStage.AppliedFields,
        CursorStage.AppliedLinks,
        CursorStage.UploadedAttachments,
    ];

    /// <summary>
    /// Returns the stage that follows <paramref name="currentStage"/> in execution
    /// order, or <see langword="null"/> if <paramref name="currentStage"/> is the
    /// last stage (returns <c>Completed</c> for the last pipeline stage, and
    /// <see langword="null"/> for <c>Completed</c> itself).
    /// </summary>
    internal static string? GetNextStage(string? currentStage)
    {
        if (currentStage is null)
            return StageNames[0];

        if (string.Equals(currentStage, CursorStage.Completed, StringComparison.Ordinal))
            return null;

        var index = IndexOf(currentStage);
        if (index < 0)
            throw new System.IO.InvalidDataException($"Unsupported cursor stage '{currentStage}'.");

        return index + 1 < StageNames.Count
            ? StageNames[index + 1]
            : CursorStage.Completed;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="stage"/> should run
    /// during a resume from <paramref name="resumeAtStage"/>.
    /// A <see langword="null"/> <paramref name="resumeAtStage"/> means fresh start
    /// (run everything). Uses position in <see cref="StageNames"/>, not string
    /// comparison, so stage name spelling never affects ordering.
    /// </summary>
    internal static bool ShouldRunStage(string stage, string? resumeAtStage)
    {
        if (resumeAtStage is null) return true;
        var stageIdx = IndexOf(stage);
        var resumeIdx = IndexOf(resumeAtStage);
        if (stageIdx < 0 || resumeIdx < 0) return true;
        return stageIdx >= resumeIdx;
    }

    private static int IndexOf(string stage)
    {
        for (var i = 0; i < StageNames.Count; i++)
            if (string.Equals(StageNames[i], stage, StringComparison.Ordinal))
                return i;
        return -1;
    }
}
