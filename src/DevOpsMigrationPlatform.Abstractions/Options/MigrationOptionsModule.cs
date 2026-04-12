using System.Collections.Generic;
#if !NET481
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// A module entry in <see cref="MigrationOptions.Modules"/>.
/// Each entry names a module, declares one or more scopes (selection criteria),
/// and lists named extensions that can each be independently enabled.
/// </summary>
public sealed class MigrationOptionsModule
{
    /// <summary>Module name, e.g. "WorkItems".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this module participates in the current run. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Scope definitions for this module — selection criteria that determine
    /// what items the module operates on (e.g. a WIQL query for WorkItems).
    /// At least one scope is required for modules that select from a source.
    /// </summary>
    public List<MigrationOptionsScope> Scopes { get; init; } = new();

    /// <summary>
    /// Named sub-module extensions for this module.
    /// Each extension controls an independently-enabled sub-operation
    /// (e.g. "Revisions", "Links", "Attachments", "Comments", "EmbeddedImages").
    /// </summary>
    public List<MigrationOptionsExtension> Extensions { get; init; } = new();
}

/// <summary>
/// A scope entry inside a <see cref="MigrationOptionsModule"/>.
/// Scopes are mandatory selection criteria. For WorkItems the only current scope
/// type is <c>"wiql"</c>, whose <c>query</c> parameter supplies the WIQL statement.
/// </summary>
public sealed class MigrationOptionsScope
{
    /// <summary>Scope type identifier. Currently supported: <c>"wiql"</c>.</summary>
    public string Type { get; init; } = string.Empty;

#if !NET481
    /// <summary>
    /// Scope-specific parameters. For <c>"wiql"</c> scopes the only required key is <c>"query"</c>.
    /// </summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = new();
#else
    public Dictionary<string, string> Parameters { get; init; } = new();
#endif
}

/// <summary>
/// A named sub-module extension inside a <see cref="MigrationOptionsModule"/>.
/// <see cref="Type"/> identifies which sub-operation this controls
/// (e.g. "Revisions", "Links", "Attachments", "Comments", "EmbeddedImages").
/// Each extension can be individually enabled or disabled and carries its own
/// typed parameters.
/// </summary>
public sealed class MigrationOptionsExtension
{
    /// <summary>Extension type identifier, e.g. "Revisions", "Links", "Attachments", "Comments", "EmbeddedImages".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Whether this extension is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

#if !NET481
    /// <summary>
    /// Extension-specific parameters. Schema is defined and validated by the owning module.
    /// Using <see cref="JsonElement"/> preserves full type fidelity during deserialization
    /// without requiring a shared parameter type in Abstractions.
    /// </summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = new();
#else
    /// <summary>
    /// Extension-specific parameters as raw strings (net481 compatibility layer).
    /// </summary>
    public Dictionary<string, string> Parameters { get; init; } = new();
#endif
}
