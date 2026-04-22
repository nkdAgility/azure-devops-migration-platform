namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Typed container for all module configurations.
/// Each property represents exactly one module instance.
/// Bound from <c>MigrationPlatform:Modules</c>.
/// </summary>
public sealed class MigrationModulesOptions
{
    /// <summary>WorkItems module configuration.</summary>
    public WorkItemsModuleOptions WorkItems { get; set; } = new();
}
