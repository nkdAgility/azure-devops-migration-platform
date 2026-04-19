using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Abstract base class for one entry in the <c>organisations</c> array (Mode 2 config).
/// Contains only connector-agnostic fields. Connector-specific properties (URL, auth, etc.)
/// live in derived classes (e.g. <c>AzureDevOpsOrganisationEntry</c>).
/// </summary>
public abstract class OrganisationEntry
{
    /// <summary>
    /// Connector type discriminator.
    /// Supported values: <c>AzureDevOpsServices</c>, <c>TeamFoundationServer</c>, <c>Simulated</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Projects to inventory. Empty or absent = all projects in the org/collection.
    /// </summary>
    public List<string> Projects { get; set; } = new List<string>();

    /// <summary>
    /// Set to <c>false</c> to skip this entry without deleting it. Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional scopes that constrain work item discovery and filtering for this organisation.
    /// Supported scope types: <c>wiql</c> (custom WIQL base query) and <c>filter</c> (regex field filter).
    /// When absent or empty, platform defaults are used for all operations.
    /// </summary>
    public List<MigrationOptionsScope> Scopes { get; set; } = new();

    /// <summary>
    /// Creates the connector-specific <see cref="MigrationEndpointOptions"/> from this entry's fields.
    /// Each connector type provides its own mapping.
    /// </summary>
    public abstract MigrationEndpointOptions ToEndpointOptions();

    /// <summary>
    /// Validates connector-specific fields (e.g. URL, authentication).
    /// </summary>
    public abstract void ValidateConnectorFields();
}
