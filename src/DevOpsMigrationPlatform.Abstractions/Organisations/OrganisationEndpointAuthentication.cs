using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Organisations;

/// <summary>
/// Immutable, resolved authentication context for an organisation endpoint.
/// Carries only resolved values — no <c>$ENV:VARNAME</c> tokens.
/// </summary>
public sealed class OrganisationEndpointAuthentication
{
    /// <summary>Authentication scheme: <see cref="AuthenticationType.Pat"/>,
    /// <see cref="AuthenticationType.Windows"/>, or <see cref="AuthenticationType.None"/>.</summary>
    public AuthenticationType Type { get; init; }

    /// <summary>
    /// Effective PAT after <c>$ENV:VARNAME</c> expansion.
    /// Null for <see cref="AuthenticationType.Windows"/> auth — this is valid.
    /// </summary>
    public string? ResolvedAccessToken { get; init; }
}
