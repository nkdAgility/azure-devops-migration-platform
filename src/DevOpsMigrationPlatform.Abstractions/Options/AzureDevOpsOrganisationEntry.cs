using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Azure DevOps-specific organisation entry for inventory discovery.
/// Carries all ADO connection fields (URL, auth, API version).
/// </summary>
public sealed class AzureDevOpsOrganisationEntry : OrganisationEntry
{
    /// <summary>
    /// Organisation URL (Azure DevOps Services).
    /// Supports <c>$ENV:VARNAME</c> resolution.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion.</summary>
    public string ResolvedUrl => ConfigTokenResolver.Resolve(Url) ?? Url;

    /// <summary>Pinned REST API version (e.g. <c>7.1</c>).</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Authentication details for this entry.</summary>
    public EndpointAuthenticationOptions Authentication { get; set; } = new EndpointAuthenticationOptions();

    /// <inheritdoc/>
    public override MigrationEndpointOptions ToEndpointOptions()
    {
        return new AzureDevOpsEndpointOptions
        {
            Type = Type,
            Url = Url,
            Project = string.Empty,
            ApiVersion = ApiVersion,
            Authentication = Authentication
        };
    }

    /// <inheritdoc/>
    public override void ValidateConnectorFields()
    {
        if (string.IsNullOrWhiteSpace(Url))
            throw new System.InvalidOperationException(
                $"Config error: An organisations entry of type '{Type}' is missing 'url'.");

        var resolvedUrl = ResolvedUrl;
        if (resolvedUrl.Contains("$ENV:"))
            throw new System.InvalidOperationException(
                $"Config error: The organisations entry URL '{Url}' contains an unresolved environment variable.");

        if (string.IsNullOrWhiteSpace(resolvedUrl))
            throw new System.InvalidOperationException(
                $"Config error: URL for a '{Type}' entry resolved to an empty string. " +
                "Set 'url' to a literal value or '$ENV:VARNAME'.");

        if (Authentication != null &&
            Authentication.Type == AuthenticationType.Pat)
        {
            var resolved = Authentication.ResolvedAccessToken;
            if (string.IsNullOrWhiteSpace(resolved))
                throw new System.InvalidOperationException(
                    $"Config error: PAT for '{resolvedUrl}' resolved to an empty string. " +
                    "Set 'authentication.accessToken' to a literal value or '$ENV:VARNAME'.");
        }
    }
}
