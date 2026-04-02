namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>A module entry in a MigrationJob.</summary>
public class MigrationJobModule
{
    /// <summary>Module name, e.g. "WorkItems".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Scope configurations for this module.</summary>
    public System.Collections.Generic.List<MigrationJobModuleScope> Scopes { get; init; } = new();
}

/// <summary>A single scope configuration — type + parameters bag.</summary>
public class MigrationJobModuleScope
{
    /// <summary>Scope type, e.g. "wiql".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Scope-specific parameters. Schema is per module type.</summary>
    public System.Collections.Generic.Dictionary<string, object?> Parameters { get; init; } = new();
}
