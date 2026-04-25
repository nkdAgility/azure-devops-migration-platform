namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Marker interface for module-specific configuration options.
///
/// Each module assembly declares a class implementing this interface for its
/// own section of the configuration file.  The section path follows the convention:
///   <c>Modules:{ModuleName}</c>
///
/// Module assemblies register their options via:
///   services.AddModuleOptions&lt;TModuleOptions&gt;(configuration, "WorkItems")
///
/// Modules then inject their config as:
///   IOptions&lt;WorkItemsModuleOptions&gt;  (constructor injection)
///
/// See docs/configuration.md for the full config file format.
/// </summary>
public interface IModuleOptions
{
    /// <summary>
    /// Whether this module participates in the current run.
    /// Modules with <c>Enabled = false</c> are skipped by the orchestrator.
    /// Default should be <c>true</c> in each implementing class.
    /// </summary>
    bool Enabled { get; }
}
