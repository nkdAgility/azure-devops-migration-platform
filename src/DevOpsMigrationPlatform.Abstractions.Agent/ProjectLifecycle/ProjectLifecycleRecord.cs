// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

public enum ProjectLifecycleCreateResult
{
    Succeeded = 0,
    Failed = 1
}

public enum ProjectLifecycleTeardownResult
{
    Succeeded = 0,
    Failed = 1,
    Skipped = 2
}

/// <summary>
/// Run-correlated lifecycle evidence record.
/// </summary>
public sealed class ProjectLifecycleRecord
{
    public string RunId { get; init; } = string.Empty;
    public string ConnectorType { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public bool ProjectOwnedByRun { get; init; } = true;

    public ProjectLifecycleCreateResult CreateResult { get; init; } = ProjectLifecycleCreateResult.Succeeded;
    public string? CreateFailureReason { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }

    public ProjectLifecycleTeardownResult TeardownResult { get; init; } = ProjectLifecycleTeardownResult.Skipped;
    public string? TeardownBlockingReason { get; init; }
    public string? PartialCleanupDetail { get; init; }
    public DateTimeOffset? TeardownAttemptedAtUtc { get; init; }
    public TimeSpan? TeardownLatency { get; init; }

    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static ProjectLifecycleRecord CreateFailed(
        ProjectLifecycleContext context,
        string reason)
    {
        return new ProjectLifecycleRecord
        {
            RunId = context.RunId,
            ConnectorType = context.ConnectorType,
            ProjectName = context.ProjectName,
            ProjectOwnedByRun = false,
            CreateResult = ProjectLifecycleCreateResult.Failed,
            CreateFailureReason = reason,
            TeardownResult = ProjectLifecycleTeardownResult.Skipped
        };
    }
}
