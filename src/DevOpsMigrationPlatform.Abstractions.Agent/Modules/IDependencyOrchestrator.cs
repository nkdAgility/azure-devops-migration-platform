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
    Task AnalyseAsync(
        IDependencyDiscoveryService dependencyService,
        OrganisationsAnalyseContext context,
        JobPolicies policies,
        int checkpointIntervalSeconds,
        CancellationToken ct);
}
