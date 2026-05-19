// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

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

        var nextStage = GetNextStage(cursor.Stage);
        return nextStage is null || string.Equals(nextStage, CursorStage.Completed, StringComparison.Ordinal)
            ? ImportResumeDecision.Skip
            : new ImportResumeDecision(false, nextStage);
    }

    private static string NormalizeFolderPath(string path)
        => path.Trim().Replace('\\', '/').TrimEnd('/');

    private static string? GetNextStage(string? currentStage) => currentStage switch
    {
        CursorStage.CreatedOrUpdated => CursorStage.AppliedFields,
        CursorStage.AppliedFields => CursorStage.AppliedLinks,
        CursorStage.AppliedLinks => CursorStage.UploadedAttachments,
        CursorStage.UploadedAttachments => CursorStage.Completed,
        CursorStage.Completed => null,
        _ => throw new InvalidDataException($"Unsupported cursor stage '{currentStage}'.")
    };
}
