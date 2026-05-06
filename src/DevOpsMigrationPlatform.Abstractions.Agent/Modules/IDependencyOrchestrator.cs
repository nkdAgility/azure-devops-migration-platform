// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Orchestrates dependency discovery export — enumerates cross-project dependencies,
/// writes CSV/analysis artefacts, and manages checkpointing.
/// </summary>
public interface IDependencyOrchestrator
{
    /// <summary>
    /// Runs a full multi-project dependency analysis (fan-in consolidation step).
    /// Reads per-project artefacts if present, otherwise queries live.
    /// </summary>
    Task AnalyseAsync(
        IDependencyDiscoveryService dependencyService,
        OrganisationsAnalyseContext context,
        JobPolicies policies,
        int checkpointIntervalSeconds,
        CancellationToken ct);

    /// <summary>
    /// Captures dependency data for a single org+project pair.
    /// Writes results to <c>discovery/{orgSlug}/{projectSlug}/dependencies.csv</c>.
    /// Called once per <c>capture.dependencies.{org}.{project}</c> plan task.
    /// </summary>
    Task CaptureProjectAsync(
        IDependencyDiscoveryService dependencyService,
        InventoryContext context,
        JobPolicies policies,
        CancellationToken ct);
}
