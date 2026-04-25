using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;

/// <summary>
/// TFS / Azure DevOps Server–specific endpoint connection options.
/// Inherits <see cref="MigrationEndpointOptions"/> and carries TFS connection fields.
/// </summary>
public sealed class TeamFoundationServerEndpointOptions : MigrationEndpointOptions
{
    /// <summary>Collection URL for the TFS instance.
    /// May contain a <c>$ENV:VARNAME</c> reference — use <see cref="ResolvedUrl"/> for API calls.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl => ConfigTokenResolver.Resolve(Url) ?? string.Empty;

    /// <summary>Team project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// REST API version to request (e.g. <c>7.1</c>).
    /// Leave empty to use the server-negotiated default.
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Authentication credentials for this endpoint.
    /// </summary>
    public EndpointAuthenticationOptions? Authentication { get; set; }

    /// <inheritdoc/>
    public override void ValidateEndpointFields(List<string> errors, string role)
    {
        if (string.IsNullOrWhiteSpace(Url))
            errors.Add($"{role}.Url is required.");
    }

    /// <inheritdoc/>
    public override string GetEndpointUrl() => Url;

    /// <inheritdoc/>
    public override string GetResolvedUrl() => ResolvedUrl;

    /// <inheritdoc/>
    public override string GetProject() => Project;

    /// <inheritdoc/>
    public override OrganisationEndpoint ToOrganisationEndpoint() => new()
    {
        ResolvedUrl = ResolvedUrl,
        Type = Type,
        ApiVersion = ApiVersion,
        Authentication = new OrganisationEndpointAuthentication
        {
            Type = Authentication?.Type ?? AuthenticationType.None,
            ResolvedAccessToken = Authentication?.ResolvedAccessToken
        }
    };
}
