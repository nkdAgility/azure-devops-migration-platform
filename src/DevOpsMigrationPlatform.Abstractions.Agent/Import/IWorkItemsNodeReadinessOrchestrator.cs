// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Ensures node readiness requirements before WorkItems revision replay.
/// </summary>
public interface IWorkItemsNodeReadinessOrchestrator
{
    /// <summary>
    /// Ensures node readiness for the supplied project mapping.
    /// </summary>
    Task EnsureReadyAsync(
        ProjectMapping nodeReadinessContext,
        bool replicateSourceTree,
        ImportContext context,
        string sourceOrgUrl,
        string sourceProjectName,
        CancellationToken ct);
}

