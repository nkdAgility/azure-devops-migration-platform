using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Job endpoint for Team Foundation Server connections.
/// Carries the collection URL, project, and authentication credentials.
/// TFS does not use an API version field.
/// </summary>
public sealed class TfsJobEndpoint : JobEndpoint
{
    /// <summary>Collection URL. May contain a <c>$ENV:VARNAME</c> reference — use <see cref="ResolvedUrl"/> for API calls.</summary>
    public string Url { get; init; } = string.Empty;

    /// <inheritdoc/>
    public override string ResolvedUrl => TokenResolver.Resolve(Url) ?? string.Empty;

    /// <inheritdoc/>
    public override string Project { get; init; } = string.Empty;

    /// <inheritdoc/>
    public override EndpointAuthenticationOptions? Authentication { get; init; }
}
