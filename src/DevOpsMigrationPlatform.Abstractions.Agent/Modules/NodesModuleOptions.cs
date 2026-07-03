// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Processing aspect for the NodesModule.</summary>
public sealed class NodesProcessingOptions
{
    /// <summary>When true, the full source classification tree is replicated to the target during import.</summary>
    public bool ReplicateSourceTree { get; init; }
}

/// <summary>Options for the NodesModule (ConfigVersion 2.0 anatomy).</summary>
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

    /// <summary>Processing aspect: import-phase tree replication policy.</summary>
    public NodesProcessingOptions Processing { get; init; } = new();
}
