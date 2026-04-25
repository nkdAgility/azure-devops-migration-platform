namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>Which discovery operations the agent should perform for a <see cref="DiscoveryJob"/>.</summary>
public enum DiscoveryJobType
{
    /// <summary>Count work items and revisions per project. Writes <c>inventory.csv</c> and <c>inventory.json</c>.</summary>
    Inventory = 0,

    /// <summary>Analyse cross-project and cross-organisation work item links. Writes <c>dependencies.csv</c>.</summary>
    Dependencies = 1,

    /// <summary>Run <see cref="Inventory"/> then <see cref="Dependencies"/> in sequence.</summary>
    Both = 2
}
