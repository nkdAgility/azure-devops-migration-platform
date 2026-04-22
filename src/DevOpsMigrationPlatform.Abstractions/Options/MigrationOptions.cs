using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Top-level platform configuration options for migration commands.
/// Bound from the <c>MigrationPlatform</c> configuration section.
///
/// Example migration.json:
/// <code>
/// {
///   "MigrationPlatform": {
///     "ConfigVersion": "1.0",
///     "Policies": { "Retries": { "Max": 8 }, "Throttle": { "MaxConcurrency": 4 }, "Checkpoints": { "Interval": 300 } },
///     "Mode": "Export",
///     "Source": { "Type": "AzureDevOpsServices", "Url": "...", "Project": "..." },
///     "Package": { "Path": "D:\\exports\\run-001" },
///     "Modules": {
///       "WorkItems": {
///         "Enabled": true,
///         "Scope": {
///           "Query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]",
///           "Filters": []
///         },
///         "Extensions": {
///           "Revisions": { "Enabled": true },
///           "Links": { "Enabled": true },
///           "Attachments": { "Enabled": true },
///           "Comments": { "Enabled": true }
///         }
///       }
///     }
///   }
/// }
/// </code>
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>Export, Import, or Both.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Source system connection. Required when Mode is Export or Both.</summary>
    public MigrationEndpointOptions? Source { get; set; }

    /// <summary>Target system connection. Required when Mode is Import or Both.</summary>
    public MigrationEndpointOptions? Target { get; set; }

    /// <summary>Package location settings.</summary>
    public MigrationPackageOptions Package { get; set; } = new();

    /// <summary>Retry, throttle, and checkpoint policies.</summary>
    public MigrationPoliciesOptions Policies { get; set; } = new();

    /// <summary>
    /// Typed module configurations. Each property represents exactly one module.
    /// When no modules are configured, the job engine applies platform defaults.
    /// </summary>
    public MigrationModulesOptions Modules { get; set; } = new();
}
