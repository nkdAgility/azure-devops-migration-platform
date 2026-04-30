namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>What operation the agent should perform for this job.</summary>
public enum JobKind
{
    /// <summary>Export source system data to the package.</summary>
    Export,

    /// <summary>Import package data into the target system.</summary>
    Import,

    /// <summary>Export then Import in sequence.</summary>
    Migrate,

    /// <summary>Count work items and revisions per project. Writes <c>inventory.csv</c> and <c>inventory.json</c>.</summary>
    Inventory,

    /// <summary>Analyse cross-project and cross-organisation work item links. Writes <c>dependencies.csv</c>.</summary>
    Dependencies,

    /// <summary>Run connectivity and permission checks against source and/or target, then write a prepare-probe file.</summary>
    Prepare
}
