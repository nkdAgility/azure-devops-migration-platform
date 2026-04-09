using System.Collections.Generic;
#if !NET481
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// A module entry in <see cref="MigrationOptions.Modules"/>.
/// Each entry names a module and provides one or more scope configurations.
/// </summary>
public sealed class MigrationOptionsModule
{
    /// <summary>Module name, e.g. "WorkItems".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this module participates in the current run. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>One or more scope configurations for this module.</summary>
    public List<MigrationOptionsScope> Scopes { get; init; } = new();
}

/// <summary>
/// A single scope configuration inside a <see cref="MigrationOptionsModule"/>.
/// The <see cref="Type"/> identifies the scope kind (e.g. "wiql"); the module
/// deserialises <see cref="Parameters"/> into its own strongly-typed parameters
/// class at execution time.
/// </summary>
public sealed class MigrationOptionsScope
{
    /// <summary>Scope type identifier, e.g. "wiql", "all", "include".</summary>
    public string Type { get; init; } = string.Empty;

#if !NET481
    /// <summary>
    /// Scope-specific parameters. Schema is defined and validated by the owning module.
    /// Using <see cref="JsonElement"/> preserves full type fidelity during deserialization
    /// without requiring a shared parameter type in Abstractions.
    /// </summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = new();
#else
    /// <summary>
    /// Scope-specific parameters as raw strings (net481 compatibility layer).
    /// </summary>
    public Dictionary<string, string> Parameters { get; init; } = new();
#endif
}
