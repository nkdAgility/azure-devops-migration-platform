namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Top-level platform configuration options.
/// Deserialised from the user's <c>migration.json</c> overlaid on the bundled
/// <c>appsettings.json</c> defaults.
///
/// Bound to the root of <c>IConfiguration</c> — there is no section wrapper.
/// Module-specific options are bound separately by each module assembly to
/// <c>Modules:{ModuleName}</c> and are NOT represented here.
///
/// Example migration.json:
/// <code>
/// {
///   "ConfigVersion": "1.0",
///   "Mode": "Export",
///   "Source": { "Type": "AzureDevOpsServices", "Url": "...", "Project": "..." },
///   "Artefacts": { "Path": "D:\\exports\\run-001" },
///   "Modules": {
///     "WorkItems": { "Enabled": true, "Query": "SELECT ..." }
///   }
/// }
/// </code>
/// </summary>
public class MigrationOptions
{
    /// <summary>Config schema version. Incremented on breaking changes to this schema.</summary>
    public string ConfigVersion { get; set; } = "1.0";

    /// <summary>Export, Import, or Both.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Source system connection. Required when Mode is Export or Both.</summary>
    public MigrationEndpointOptions? Source { get; set; }

    /// <summary>Target system connection. Required when Mode is Import or Both.</summary>
    public MigrationEndpointOptions? Target { get; set; }

    /// <summary>Package location settings.</summary>
    public MigrationArtefactsOptions Artefacts { get; set; } = new();

    /// <summary>Retry and throttle policies.</summary>
    public MigrationPoliciesOptions Policies { get; set; } = new();
}
