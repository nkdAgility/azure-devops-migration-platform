#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Options for the NodesModule.</summary>
#if NET7_0_OR_GREATER
public sealed class NodesModuleOptions : IConfigSection
#else
public sealed class NodesModuleOptions
#endif
{
    /// <summary>Configuration section name.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Nodes";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When true, the full source classification tree is replicated to the target during import.
    /// </summary>
    public bool ReplicateSourceTree { get; init; }
}
