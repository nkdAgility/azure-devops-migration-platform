// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// A snapshot of discovery data for a single Azure DevOps project.
/// Yielded incrementally by <see cref="ICatalogService.CountAllWorkItemsAsync"/>.
/// </summary>
public class ProjectDiscoverySummary
{
    public string ProjectName { get; set; } = string.Empty;

    public int WorkItemsCount { get; set; }
    public int RevisionsCount { get; set; }
    public int ReposCount { get; set; }
    public int PipelinesCount { get; set; }

    public bool IsWorkItemComplete { get; set; }
    public bool IsRepoComplete { get; set; }
    public bool IsPipelineComplete { get; set; }

    /// <summary>
    /// Non-null when work-item discovery failed mid-scan.
    /// Counts represent partial data collected before the failure.
    /// </summary>
    public string? Error { get; set; }

    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
