// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;

/// <summary>
/// Entry point for deterministic work item revision replay during import.
/// </summary>
public sealed class WorkItemRevisionImporter
{
    private readonly WorkItemsImportRuntime _orchestrator;

    public WorkItemRevisionImporter(WorkItemsImportRuntime orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public Task ExecuteAsync(
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
        => _orchestrator.ImportAsync(ext, resumeMode, ct);
}
