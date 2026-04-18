using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Pairs an <see cref="OrganisationEndpoint"/> with the list of projects to target.
/// Lives on <see cref="DiscoveryJob.Organisations"/> only.
/// Factory implementations extract <see cref="Endpoint"/> for service calls and
/// <see cref="Projects"/> for scope filtering.
/// </summary>
public sealed class ScopedOrganisationEndpoint
{
    /// <summary>Connection context (resolved URL + auth + type + API version).</summary>
    public OrganisationEndpoint Endpoint { get; init; } = new();

    /// <summary>Projects to target. Empty = all projects in the organisation.</summary>
    public List<string> Projects { get; init; } = new();
}
