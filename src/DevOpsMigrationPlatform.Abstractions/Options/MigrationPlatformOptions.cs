// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Options;

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
public sealed class MigrationPlatformOptions
{
    /// <summary>Schema version of this configuration file (e.g. "2.0").</summary>
    public string ConfigVersion { get; set; } = string.Empty;

    /// <summary>Inventory, Dependencies, Export, Prepare, Import, or Migrate.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Source system connection. Required when Mode is Export or Migrate.</summary>
    public MigrationEndpointOptions? Source { get; set; }

    /// <summary>Target system connection. Required when Mode is Prepare, Import, or Migrate.</summary>
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

    /// <summary>Organisations / collections to discover. Required when Mode is Inventory or Dependencies.</summary>
    public List<OrganisationEntry> Organisations { get; set; } = new();

    /// <summary>
    /// Returns only the organisations with <see cref="OrganisationEntry.Enabled"/> set to <c>true</c>.
    /// Callers that act on organisations (discovery, inventory) should iterate this projection so the
    /// enabled/disabled business rule is applied in one place rather than at each call site.
    /// </summary>
    public IEnumerable<OrganisationEntry> EnabledOrganisations()
    {
        foreach (var org in Organisations)
        {
            if (org.Enabled)
                yield return org;
        }
    }
}
