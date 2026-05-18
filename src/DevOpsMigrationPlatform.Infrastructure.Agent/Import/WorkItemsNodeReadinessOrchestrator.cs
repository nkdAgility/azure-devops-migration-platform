// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Default node readiness orchestration for WorkItems import.
/// </summary>
public sealed class WorkItemsNodeReadinessOrchestrator : IWorkItemsNodeReadinessOrchestrator
{
    private readonly NodeReadinessOrchestrator? _nodeReadinessOrchestrator;
    private readonly INodesOrchestrator? _nodesOrchestrator;
    private readonly IPlatformMetrics? _metrics;
    private readonly ILogger _logger;

    public WorkItemsNodeReadinessOrchestrator(
        NodeReadinessOrchestrator? nodeReadinessOrchestrator,
        INodesOrchestrator? nodesOrchestrator,
        IPlatformMetrics? metrics,
        ILogger logger)
    {
        _nodeReadinessOrchestrator = nodeReadinessOrchestrator;
        _nodesOrchestrator = nodesOrchestrator;
        _metrics = metrics;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureReadyAsync(
        ProjectMapping nodeReadinessContext,
        bool replicateSourceTree,
        ImportContext context,
        string sourceOrgUrl,
        string sourceProjectName,
        CancellationToken ct)
    {
        if (_nodeReadinessOrchestrator is not null)
        {
            await _nodeReadinessOrchestrator
                .ExecuteAsync(nodeReadinessContext, replicateSourceTree, ct)
                .ConfigureAwait(false);
            return;
        }

        if (_nodesOrchestrator is not null)
        {
            _logger.LogWarning("[WorkItems] NodeReadinessOrchestrator is not available — falling back to INodesOrchestrator.EnsureReferencedPathsAsync.");
            await _nodesOrchestrator
                .EnsureReferencedPathsAsync(
                    nodeReadinessContext,
                    context.Package,
                    sourceOrgUrl,
                    sourceProjectName,
                    ct,
                    _metrics,
                    context.Job.JobId)
                .ConfigureAwait(false);
            return;
        }

        _logger.LogWarning("[WorkItems] No node readiness orchestrator is available — node readiness dispatch will be skipped.");
    }
}

