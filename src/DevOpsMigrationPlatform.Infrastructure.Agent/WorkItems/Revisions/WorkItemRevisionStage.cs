// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

/// <summary>
/// Describes one cursor-bearing extension stage in the per-revision import pipeline.
/// Core stages (CreatedOrUpdated, AppliedFields) are handled inline due to their
/// complex early-return control flow; extension stages use this descriptor.
/// </summary>
internal sealed record WorkItemRevisionStage(
    string CursorName,
    Func<bool> IsEnabled,
    Func<WorkItemExtensionContext, CancellationToken, Task> ExecuteAsync
);
