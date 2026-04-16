namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// A module entry in a <see cref="MigrationJob"/>.
/// Carries one or more scopes (selection criteria) and a list of named extensions
/// that independently control each sub-operation.
/// </summary>
public class JobModule
{
    /// <summary>Module name, e.g. "WorkItems".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Scope definitions — mandatory selection criteria.
    /// For WorkItems the only current type is <c>"wiql"</c> with a <c>"query"</c> parameter.
    /// </summary>
    public System.Collections.Generic.List<JobModuleScope> Scopes { get; init; } = new();

    /// <summary>
    /// Named sub-module extensions for this module.
    /// Each entry controls an independently-enabled sub-operation.
    /// </summary>
    public System.Collections.Generic.List<JobModuleExtension> Extensions { get; init; } = new();
}

/// <summary>
/// A scope entry in a <see cref="JobModule"/> — selection criteria for the module.
/// </summary>
public class JobModuleScope
{
    /// <summary>Scope type, e.g. <c>"wiql"</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Scope-specific parameters. For <c>"wiql"</c> scopes: <c>"query"</c> is required.</summary>
    public System.Collections.Generic.Dictionary<string, object?> Parameters { get; init; } = new();
}

/// <summary>
/// A named sub-module extension — type, enabled flag, and parameters bag.
/// </summary>
public class JobModuleExtension
{
    /// <summary>Extension type, e.g. "Revisions", "Links", "Attachments", "Comments", "EmbeddedImages".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Whether this extension participates in the current run. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Extension-specific parameters. Schema is per module type.</summary>
    public System.Collections.Generic.Dictionary<string, object?> Parameters { get; init; } = new();
}
