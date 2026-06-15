// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

/// <summary>
/// Carries the per-job resources needed by the revision-folder import loop.
/// Avoids parameter explosion across private helpers in WorkItemsOrchestrator.
/// </summary>
internal sealed record WorkItemRevisionJobScope(
    IPackageAccess Package,
    string Organisation,
    string Project,
    ICheckpointingService Checkpointing,
    IProgressSink ProgressSink,
    IWorkItemResolutionStrategy ResolutionStrategy,
    IIdMapStore IdMapStore,
    IWorkItemResolutionProcessor Processor,
    IWorkItemTarget Target,
    string? JobId,
    IReadOnlyList<WorkItemFieldFilterOptions>? FilterOptions);
