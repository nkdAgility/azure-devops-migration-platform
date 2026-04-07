using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// One entry in the <c>organisations</c> array (Mode 2 config).
/// </summary>
public sealed class OrganisationEntry
{
    /// <summary>
    /// Source type. Supported values: <c>AzureDevOpsServices</c>, <c>TeamFoundationServer</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Organisation URL (Azure DevOps Services) or collection URL (TFS).
    /// Supports <c>$ENV:VARNAME</c> resolution via <see cref="TokenResolver"/>.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The effective URL after <c>$ENV:VARNAME</c> expansion.
    /// Use this instead of <see cref="Url"/> when making API calls.
    /// </summary>
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? Url;

    /// <summary>
    /// Projects to inventory. Empty or absent = all projects in the org/collection.
    /// </summary>
    public List<string> Projects { get; set; } = new List<string>();

    /// <summary>Pinned REST API version (e.g. <c>7.1</c>).</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Authentication details for this entry.</summary>
    public EndpointAuthenticationOptions Authentication { get; set; } = new EndpointAuthenticationOptions();

    /// <summary>
    /// Set to <c>false</c> to skip this entry without deleting it. Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
