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
    public const string InventoryRevisions = "discovery.inventory.revisions";
    public const string InventoryRepos = "discovery.inventory.repos";

    // --- Dependencies ---
    public const string DependencyLinks = "discovery.dependencies.links";
    public const string DependencyWorkItemsAnalysed = "discovery.dependencies.workitems_analysed";

    // --- Operational ---
    public const string CheckpointsSaved = "discovery.checkpoints.saved";
    public const string JobDurationMs = "discovery.job.duration_ms";
    public const string JobsActive = "discovery.jobs.active";
}
