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
    /// Creates an immutable <see cref="OrganisationEndpoint"/> from this entry's connection fields.
    /// Each connector type provides its own mapping.
    /// </summary>
    public abstract OrganisationEndpoint ToOrganisationEndpoint();

    /// <summary>
    /// Validates connector-specific fields (e.g. URL, authentication).
    /// </summary>
    public abstract void ValidateConnectorFields();
}
