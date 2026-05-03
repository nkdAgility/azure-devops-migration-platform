// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Scope-level counters shared by all job types (migration and discovery).
/// Tracks organisation and project progress at aggregate level.
/// </summary>
public record JobScopeCounters
{
    /// <summary>Total organisations in the job.</summary>
    public int OrganisationsTotal { get; init; }

    /// <summary>Organisations completed successfully.</summary>
    public int OrganisationsCompleted { get; init; }

    /// <summary>Organisations that failed.</summary>
    public int OrganisationsFailed { get; init; }

    /// <summary>Total projects in the job.</summary>
    public int ProjectsTotal { get; init; }

    /// <summary>Projects completed successfully.</summary>
    public int ProjectsCompleted { get; init; }

    /// <summary>Projects that failed.</summary>
    public int ProjectsFailed { get; init; }

    /// <summary>Total work items known across all projects.</summary>
    public long WorkItemsTotal { get; init; }
}
