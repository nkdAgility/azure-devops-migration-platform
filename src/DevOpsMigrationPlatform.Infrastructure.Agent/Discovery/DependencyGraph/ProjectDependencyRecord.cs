// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Aggregated dependency record summarising all links between a specific pair of projects.
/// Written to grouped.csv and used for Mermaid diagram generation.
/// </summary>
public record ProjectDependencyRecord
{
    /// <summary>
    /// Gets the name of the source project.
    /// </summary>
    public string SourceProject { get; set; } = "";

    /// <summary>
    /// Gets the name of the target project (empty for cross-organisation targets).
    /// </summary>
    public string TargetProject { get; set; } = "";

    /// <summary>
    /// Gets the hostname or organisation URL of the target (empty for same-org targets).
    /// </summary>
    public string TargetOrganisation { get; set; } = "";

    /// <summary>
    /// Gets the total number of distinct links between this source and target pair.
    /// </summary>
    public int LinkCount { get; set; }

    /// <summary>
    /// Gets the scope of the links (CrossProject or CrossOrganisation).
    /// </summary>
    public LinkScope LinkScope { get; set; }

    /// <summary>
    /// Gets the component group ID assigned by Union-Find. All projects in the same connected component share the same GroupId.
    /// </summary>
    public int GroupId { get; set; }

    /// <summary>
    /// Gets the date of the most recently changed link between this source and target pair.
    /// <c>null</c> when no link-level timestamps are available.
    /// </summary>
    public DateTimeOffset? MostRecentLinkDate { get; set; }

    /// <summary>
    /// Gets the most recent <c>System.ChangedDate</c> observed on source work items
    /// participating in this source-target pair.
    /// </summary>
    public DateTimeOffset? MostRecentSourceWorkItemChangedDate { get; set; }

    /// <summary>
    /// Gets the number of links broken down by the source work item type (e.g. "User Story", "Bug", "Task").
    /// Keys are normalised to the values returned by the source system; the sum equals <see cref="LinkCount"/>.
    /// </summary>
    public Dictionary<string, int> LinkCountByType { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the number of links whose source work item has a state category of <c>InProgress</c>.
    /// These represent actively-worked items spanning the project boundary and carry the highest migration risk.
    /// </summary>
    public int ActiveLinkCount { get; set; }

    /// <summary>
    /// Creates a ProjectDependencyRecord from a ProjectPairKey and link count.
    /// </summary>
    public ProjectDependencyRecord(ProjectPairKey key, int linkCount)
    {
        SourceProject = key.SourceProject;
        TargetProject = key.TargetProject;
        TargetOrganisation = key.TargetOrganisation;
        LinkCount = linkCount;
        LinkScope = key.LinkScope;
    }

    public ProjectDependencyRecord()
    {
    }
}
