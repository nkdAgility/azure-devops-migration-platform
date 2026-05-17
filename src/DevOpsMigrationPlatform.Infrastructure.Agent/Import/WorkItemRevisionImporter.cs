// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Entry point for deterministic work item revision replay during import.
/// </summary>
public sealed class WorkItemRevisionImporter
{
    private readonly WorkItemImportOrchestrator _orchestrator;

    public WorkItemRevisionImporter(WorkItemImportOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public Task ExecuteAsync(
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
        => _orchestrator.ImportAsync(ext, resumeMode, ct);
}
#endif
