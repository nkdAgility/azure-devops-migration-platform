// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System.IO;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

internal static class ImportResumeDecisionResolver
{
    internal static ResumeDecision Resolve(string folderPath, CursorEntry? cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var normalizedFolderPath = NormalizeFolderPath(folderPath);

        if (cursor is null || string.IsNullOrWhiteSpace(cursor.LastProcessed))
            return ResumeDecision.StartFromBeginning;

        var normalizedLastProcessed = NormalizeFolderPath(cursor.LastProcessed);
        var comparison = string.CompareOrdinal(normalizedFolderPath, normalizedLastProcessed);
        if (comparison < 0)
            return ResumeDecision.Skip;

        if (comparison > 0)
            return ResumeDecision.StartFromBeginning;

        if (string.Equals(cursor.Stage, CursorStage.Completed, StringComparison.Ordinal))
            return ResumeDecision.Skip;

        var nextStage = GetNextStage(cursor.Stage);
        return nextStage is null || string.Equals(nextStage, CursorStage.Completed, StringComparison.Ordinal)
            ? ResumeDecision.Skip
            : new ResumeDecision(ShouldSkip: false, ResumeAtStage: nextStage);
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
#endif
