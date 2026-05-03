// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Per-project result record written to <c>inventory.csv</c> on completion.
/// All projects are written, including those that failed mid-count.
/// </summary>
public sealed class InventorySummary
{
    public string Url { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public long WorkItemsCount { get; set; }
    public long RevisionsCount { get; set; }
    public long ReposCount { get; set; }
    public long PipelinesCount { get; set; }
    /// <summary>True when all date windows were scanned without error.</summary>
    public bool IsComplete { get; set; }
    /// <summary>Non-null when counting failed for this project.</summary>
    public string? Error { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
