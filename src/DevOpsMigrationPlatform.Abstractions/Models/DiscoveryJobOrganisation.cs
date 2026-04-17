using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// One organisation / collection entry inside a <see cref="DiscoveryJob"/>.
/// Mirrors <see cref="OrganisationEntry"/> but is a sealed, init-only record
/// suitable for serialisation and inter-component handoff.
/// </summary>
public class DiscoveryJobOrganisation
{
    /// <summary>Source type. Supported value: <c>AzureDevOpsServices</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Organisation URL. May contain a <c>$ENV:VARNAME</c> reference.
    /// Use <see cref="ResolvedUrl"/> for API calls.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl => TokenResolver.Resolve(Url) ?? Url;

    /// <summary>
    /// Projects to target. Empty = all projects in the organisation.
    /// </summary>
    public List<string> Projects { get; init; } = new();

    /// <summary>Pinned REST API version (e.g. <c>7.1</c>).</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Authentication credentials for this organisation.</summary>
    public DiscoveryJobAuthentication Authentication { get; init; } = new();
}

/// <summary>Authentication credentials carried inside a <see cref="DiscoveryJobOrganisation"/>.</summary>
public class DiscoveryJobAuthentication
{
    /// <summary>Authentication scheme. Currently only <c>Pat</c> is supported.</summary>
    public string Type { get; init; } = "Pat";

    /// <summary>
    /// Personal Access Token. May contain a <c>$ENV:VARNAME</c> reference.
    /// Use <see cref="ResolvedAccessToken"/> for API calls.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>The effective token after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedAccessToken => TokenResolver.Resolve(AccessToken) ?? AccessToken;
}
