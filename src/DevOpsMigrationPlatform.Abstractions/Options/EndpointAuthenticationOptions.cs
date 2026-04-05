namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Authentication details for a source, target, or organisations entry.
/// </summary>
public sealed class EndpointAuthenticationOptions
{
    /// <summary>
    /// Authentication type. Supported values: <c>Pat</c>, <c>Windows</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Personal Access Token (or <c>$ENV:VARNAME</c> reference).
    /// Resolved at runtime by <see cref="DevOpsMigrationPlatform.Abstractions.Utilities.TokenResolver"/>.
    /// Required when <see cref="Type"/> is <c>Pat</c>; ignored for <c>Windows</c>.
    /// </summary>
    public string? AccessToken { get; set; }
}
