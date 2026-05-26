// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

/// <summary>
/// Emits structured progress for lifecycle outcomes.
/// </summary>
public sealed class ProjectLifecycleProgressEmitter
{
    private readonly ILogger<ProjectLifecycleProgressEmitter> _logger;

    public ProjectLifecycleProgressEmitter(ILogger<ProjectLifecycleProgressEmitter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Emit(ProjectLifecycleRecord record)
    {
        _logger.LogInformation(
            "ProjectLifecycle outcome runId={RunId} connector={ConnectorType} project={ProjectName} create={CreateResult} teardown={TeardownResult} teardownReason={TeardownBlockingReason} partialCleanup={PartialCleanupDetail} latencyMs={TeardownLatencyMs}",
            record.RunId,
            record.ConnectorType,
            record.ProjectName,
            record.CreateResult,
            record.TeardownResult,
            record.TeardownBlockingReason ?? string.Empty,
            record.PartialCleanupDetail ?? string.Empty,
            record.TeardownLatency?.TotalMilliseconds ?? 0d);
    }
}
