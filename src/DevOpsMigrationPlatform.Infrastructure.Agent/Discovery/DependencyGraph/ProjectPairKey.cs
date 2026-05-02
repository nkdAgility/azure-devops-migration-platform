// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited


namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Lightweight key for grouping work item dependency links by source and target project pair.
/// Used as dictionary key to accumulate link counts across all dependencies discovered during streaming.
/// </summary>
public readonly record struct ProjectPairKey
{
    /// <summary>
    /// Gets the name of the source project.
    /// </summary>
    public string SourceProject { get; init; }

    /// <summary>
    /// Gets the name of the target project (empty string for cross-organisation targets).
    /// </summary>
    public string TargetProject { get; init; }

    /// <summary>
    /// Gets the hostname or organisation URL of the target (empty string for same-org targets).
    /// </summary>
    public string TargetOrganisation { get; init; }

    /// <summary>
    /// Gets the scope of the link (CrossProject or CrossOrganisation).
    /// </summary>
    public LinkScope LinkScope { get; init; }

    /// <summary>
    /// Creates a new ProjectPairKey from a dependency record.
    /// </summary>
    public ProjectPairKey(DependencyRecord record)
    {
        SourceProject = record.SourceProject ?? "";
        TargetProject = record.TargetProject ?? "";
        TargetOrganisation = record.TargetOrganisation ?? "";
        LinkScope = record.LinkScope;
    }

    public ProjectPairKey()
    {
        SourceProject = "";
        TargetProject = "";
        TargetOrganisation = "";
        LinkScope = LinkScope.CrossProject;
    }
}
