// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>Per-work-item export progress returned by <see cref="IExportProgressStore"/>.</summary>
/// <param name="WorkItemId">The work item identifier.</param>
/// <param name="Rev">
/// The <see cref="DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemRevision.RevisionIndex"/>
/// of the last revision successfully written to the package.
/// On resume, any revision whose <c>RevisionIndex</c> is less than or equal to this value
/// has already been exported and can be safely skipped.
/// </param>
public sealed record WorkItemExportProgress(int WorkItemId, int Rev);
