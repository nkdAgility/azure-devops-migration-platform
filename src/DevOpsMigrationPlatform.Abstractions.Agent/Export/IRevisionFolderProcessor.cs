// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Processes a single revision folder through the four import stages
/// (CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed).
/// </summary>
public interface IRevisionFolderProcessor
{
    /// <summary>
    /// Process a single revision folder, resuming from <paramref name="resumeAtStage"/> if provided.
    /// </summary>
    /// <param name="folderPath">Relative folder path, e.g. <c>WorkItems/2026-01-15/638760000000000001-42-3</c>.</param>
    /// <param name="ext">Module extension flags controlling which stages run.</param>
    /// <param name="resumeAtStage">
    /// If not null, skip all stages that lexicographically precede this value.
    /// Pass <see langword="null"/> to start from Stage A.
    /// </param>
    /// <param name="resolutionStrategy">Strategy for live fallback ID lookup in Stage A.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ProcessAsync(
        string folderPath,
        WorkItemsModuleExtensions ext,
        string? resumeAtStage,
        IWorkItemResolutionStrategy resolutionStrategy,
        CancellationToken ct);
}
#endif
