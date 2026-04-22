namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Configuration for the WorkItems module.
/// Bound from <c>MigrationPlatform:Modules:WorkItems</c>.
/// </summary>
public sealed class WorkItemsModuleOptions
{
    /// <summary>Whether this module participates in the current run. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Selection scope: WIQL query and field-level filters.</summary>
    public WorkItemsScopeOptions Scope { get; init; } = new();

    /// <summary>Typed extension configurations for this module.</summary>
    public WorkItemsExtensionsOptions Extensions { get; init; } = new();
}
