using System.Collections.Generic;
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
///     "Controls": { "CheckpointInterval": 300, "MaxConcurrency": 4 },
///     "Mode": "Export",
///     "Source": { "Type": "AzureDevOpsServices", "Url": "...", "Project": "..." },
///     "Artefacts": { "Path": "D:\\exports\\run-001" },
///     "Modules": [
///       { "Name": "WorkItems", "Enabled": true,
///         "Extensions": [
///           { "Type": "Revisions", "Enabled": true },
///           { "Type": "Links", "Enabled": true },
///           { "Type": "Attachments", "Enabled": true },
///           { "Type": "Comments", "Enabled": true }
///         ] }
///     ]
///   }
/// }
/// </code>
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>Cross-cutting execution controls.</summary>
    public ControlsOptions Controls { get; set; } = new();

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

    /// <summary>
    /// Ordered list of modules to run with their scope configurations.
    /// When empty, the job engine applies platform defaults (WorkItems module with default WIQL scope).
    /// </summary>
    public List<MigrationOptionsModule> Modules { get; set; } = new();
}
