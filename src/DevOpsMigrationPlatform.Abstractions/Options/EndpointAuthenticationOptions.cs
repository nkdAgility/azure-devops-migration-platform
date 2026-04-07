namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Authentication details for a source, target, or organisations entry.
/// </summary>
public sealed class EndpointAuthenticationOptions
{
    /// <summary>Authentication type. Defaults to <see cref="AuthenticationType.None"/> so that
    /// entries which omit the <c>authentication</c> block entirely do not trigger PAT validation.</summary>
    public AuthenticationType Type { get; set; } = AuthenticationType.None;

    /// <summary>
    /// Personal Access Token (or <c>$ENV:VARNAME</c> reference).
    /// Resolved at runtime by <see cref="DevOpsMigrationPlatform.Abstractions.Utilities.TokenResolver"/>.
    /// Required when <see cref="Type"/> is <c>Pat</c>; ignored for <c>Windows</c>.
    /// </summary>
    public string? AccessToken { get; set; }
}
