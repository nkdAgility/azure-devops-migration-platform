// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
    /// <summary>Tags related to the job context.</summary>
    public static class Job
    {
        /// <summary>Unique job identifier. High cardinality — traces only, except via MigrationTagList for metrics.</summary>
        public const string Id = "job.id";

        /// <summary>Job type (e.g. "migration", "discovery"). Low cardinality.</summary>
        public const string Type = "job.type";

        /// <summary>Job state (e.g. "queued", "running", "completed"). Low cardinality.</summary>
        public const string State = "job.state";
    }

    /// <summary>Tags related to operation context.</summary>
    public static class Operation
    {
        /// <summary>Operation name: "export", "import", "validation". Low cardinality.</summary>
        public const string Name = "operation";

        /// <summary>Module name (e.g. "WorkItems", "Inventory"). Low cardinality.</summary>
        public const string Module = "module";

        /// <summary>Source type (e.g. "AzureDevOps"). Low cardinality.</summary>
        public const string SourceType = "source.type";
    }

    /// <summary>Tags related to work item entity context.</summary>
    public static class WorkItem
    {
        /// <summary>Work item type name (e.g. "Bug", "User Story"). Medium cardinality.</summary>
        public const string Type = "workitem.type";

        /// <summary>
        /// Work item integer ID. HIGH CARDINALITY — traces and logs only, never use as a metric tag.
        /// </summary>
        [System.Obsolete("WorkItemId is HIGH CARDINALITY and must not be used as a metric tag. Use in traces and logs only.")]
        public const string Id = "workitem.id";

        /// <summary>
        /// Revision index within a work item. HIGH CARDINALITY — traces and logs only, never use as a metric tag.
        /// </summary>
        [System.Obsolete("RevisionIndex is HIGH CARDINALITY and must not be used as a metric tag. Use in traces and logs only.")]
        public const string RevisionIndex = "revision.index";
    }

    /// <summary>Tags related to field transform context.</summary>
    public static class Transform
    {
        /// <summary>Transform group name. Medium cardinality.</summary>
        public const string GroupName = "group.name";
    }

    /// <summary>Tags related to CLI context.</summary>
    public static class Cli
    {
        /// <summary>CLI command name (e.g. "queue", "prepare", "tui"). Low cardinality.</summary>
        public const string Command = "command";

        /// <summary>Command exit code (0 = success). Low cardinality.</summary>
        public const string ExitCode = "exit.code";
    }
}
