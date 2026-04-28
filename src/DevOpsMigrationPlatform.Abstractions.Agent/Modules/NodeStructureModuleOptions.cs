#if !NET481
namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Options for the NodeStructureModule.</summary>
public sealed class NodeStructureModuleOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MigrationPlatform:Modules:Nodes";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// When true, the full source classification tree is replicated to the target during import.
    /// </summary>
    public bool ReplicateSourceTree { get; init; }

    /// <summary>
    /// When true, missing area/iteration nodes referenced by work items are auto-created during import.
    /// </summary>
    public bool AutoCreateNodes { get; init; }
}
#endif
