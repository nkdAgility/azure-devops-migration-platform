// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

internal static class ImportResumeDecisionResolver
{
    internal static ImportResumeDecision Resolve(string folderPath, CursorEntry? cursor)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Folder path cannot be null or whitespace.", nameof(folderPath));
        var normalizedFolderPath = NormalizeFolderPath(folderPath);

        if (cursor is null || string.IsNullOrWhiteSpace(cursor.LastProcessed))
            return ImportResumeDecision.StartFromBeginning;

        var normalizedLastProcessed = NormalizeFolderPath(cursor.LastProcessed);
        var comparison = string.CompareOrdinal(normalizedFolderPath, normalizedLastProcessed);
        if (comparison < 0)
            return ImportResumeDecision.Skip;

        if (comparison > 0)
            return ImportResumeDecision.StartFromBeginning;

        if (string.Equals(cursor.Stage, CursorStage.Completed, StringComparison.Ordinal))
            return ImportResumeDecision.Skip;

        var nextStage = WorkItemRevisionStagePipeline.GetNextStage(cursor.Stage);
        return nextStage is null || string.Equals(nextStage, CursorStage.Completed, StringComparison.Ordinal)
            ? ImportResumeDecision.Skip
            : new ImportResumeDecision(false, nextStage);
    }

    private static string NormalizeFolderPath(string path)
        => path.Trim().Replace('\\', '/').TrimEnd('/');
}
