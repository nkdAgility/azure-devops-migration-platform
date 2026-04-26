namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Canonical tag/attribute name constants for dimension tags used in metrics (<see cref="System.Diagnostics.TagList"/>)
/// and traces (<see cref="System.Diagnostics.Activity.SetTag"/>).
/// All new dimension tags MUST use constants from this class — hardcoded string literals are prohibited.
/// <para>
/// This class contains only <b>dimension tags</b> used for filtering, grouping, and correlation.
/// Per-span result attributes (counts, durations, booleans) do not belong here — they are
/// contextual data, not reusable dimensions.
/// </para>
/// </summary>
public static class WellKnownTagNames
{
    // --- Job context ---

    /// <summary>Unique job identifier. High cardinality — traces only, except via MigrationTagList for metrics.</summary>
    public const string JobId = "job.id";

    /// <summary>Job type (e.g. "migration", "discovery"). Low cardinality.</summary>
    public const string JobType = "job.type";

    /// <summary>Job state (e.g. "queued", "running", "completed"). Low cardinality.</summary>
    public const string JobState = "job.state";

    // --- Operation context ---

    /// <summary>Operation name: "export", "import", "validation". Low cardinality.</summary>
    public const string Operation = "operation";

    /// <summary>Module name (e.g. "WorkItems", "Inventory"). Low cardinality.</summary>
    public const string Module = "module";

    /// <summary>Source type (e.g. "AzureDevOps"). Low cardinality.</summary>
    public const string SourceType = "source.type";

    // --- Work item entity (high cardinality — traces and logs only) ---

    /// <summary>Work item integer ID. High cardinality — traces only.</summary>
    public const string WorkItemId = "workitem.id";

    /// <summary>Work item type name (e.g. "Bug", "User Story"). Medium cardinality.</summary>
    public const string WorkItemType = "workitem.type";

    /// <summary>Revision index within a work item. Medium cardinality.</summary>
    public const string RevisionIndex = "revision.index";

    // --- Field transform (medium cardinality — traces only) ---

    /// <summary>Transform group name. Medium cardinality.</summary>
    public const string GroupName = "group.name";
}
