// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// OpenTelemetry instrument name constants for the <c>discovery.</c> meter.
/// These names are the public contract — renaming is a breaking change requiring a version increment.
/// </summary>
public static class WellKnownDiscoveryMetricNames
{
    // --- Organisation ---
    public const string OrganisationsQueued = "discovery.organisations.queued";
    public const string OrganisationsCompleted = "discovery.organisations.completed";
    public const string OrganisationsFailed = "discovery.organisations.failed";
    public const string OrganisationDurationMs = "discovery.organisations.duration_ms";
    public const string OrganisationProjectCount = "discovery.organisations.project_count";

    // --- Project ---
    public const string ProjectsQueued = "discovery.projects.queued";
    public const string ProjectsCompleted = "discovery.projects.completed";
    public const string ProjectsFailed = "discovery.projects.failed";
    public const string ProjectDurationMs = "discovery.projects.duration_ms";

    // --- Inventory ---
    public const string InventoryWorkItems = "discovery.inventory.workitems";
    public const string InventoryWorkItemsDurationMs = "discovery.inventory.workitems.duration_ms";
    public const string InventoryWorkItemsErrors = "discovery.inventory.workitems.errors";
    public const string InventoryWorkItemsInFlight = "discovery.inventory.workitems.in_flight";
    public const string InventoryIdentities = "discovery.inventory.identities";
    public const string InventoryIdentitiesDurationMs = "discovery.inventory.identities.duration_ms";
    public const string InventoryIdentitiesErrors = "discovery.inventory.identities.errors";
    public const string InventoryIdentitiesInFlight = "discovery.inventory.identities.in_flight";
    public const string InventoryNodes = "discovery.inventory.nodes";
    public const string InventoryNodesDurationMs = "discovery.inventory.nodes.duration_ms";
    public const string InventoryNodesErrors = "discovery.inventory.nodes.errors";
    public const string InventoryNodesInFlight = "discovery.inventory.nodes.in_flight";
    public const string InventoryTeams = "discovery.inventory.teams";
    public const string InventoryTeamsDurationMs = "discovery.inventory.teams.duration_ms";
    public const string InventoryTeamsErrors = "discovery.inventory.teams.errors";
    public const string InventoryTeamsInFlight = "discovery.inventory.teams.in_flight";
    public const string InventoryConsolidated = "discovery.inventory.consolidated";
    public const string InventoryConsolidatedDurationMs = "discovery.inventory.consolidated.duration_ms";
    public const string InventoryConsolidatedErrors = "discovery.inventory.consolidated.errors";
    public const string InventoryRevisions = "discovery.inventory.revisions";
    public const string InventoryRepos = "discovery.inventory.repos";

    // --- Dependencies ---
    public const string DependencyLinks = "discovery.dependencies.links";
    public const string DependencyWorkItemsAnalysed = "discovery.dependencies.workitems_analysed";
    public const string DependenciesAnalyseDurationMs = "discovery.dependencies.analyse.duration_ms";
    public const string DependenciesAnalyseErrors = "discovery.dependencies.analyse.errors";

    // --- Operational ---
    public const string CheckpointsSaved = "discovery.checkpoints.saved";
    public const string JobDurationMs = "discovery.job.duration_ms";
    public const string JobsActive = "discovery.jobs.active";
}
