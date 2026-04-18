using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Job endpoint for Azure DevOps Services connections.
/// Carries the organisation URL, project, API version, and authentication credentials.
/// </summary>
public sealed class AzureDevOpsJobEndpoint : JobEndpoint
{
    /// <summary>Organisation URL. May contain a <c>$ENV:VARNAME</c> reference — use <see cref="ResolvedUrl"/> for API calls.</summary>
    public string Url { get; init; } = string.Empty;

    /// <inheritdoc/>
    public override string ResolvedUrl => TokenResolver.Resolve(Url) ?? string.Empty;

    /// <inheritdoc/>
    public override string Project { get; init; } = string.Empty;

    /// <inheritdoc/>
    public override string? ApiVersion { get; init; }

    /// <inheritdoc/>
    public override EndpointAuthenticationOptions? Authentication { get; init; }
}
