using DevOpsMigrationPlatform.Abstractions.Jobs;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Organisations;

/// <summary>
/// Pairs a <see cref="MigrationEndpointOptions"/> with the list of projects to target.
/// Lives on <see cref="DiscoveryJob.Organisations"/> only.
/// Factory implementations extract <see cref="Endpoint"/> for service calls and
/// <see cref="Projects"/> for scope filtering.
/// </summary>
public sealed class ScopedOrganisationEndpoint
{
    /// <summary>Connection context (resolved URL + auth + type + API version).</summary>
    public MigrationEndpointOptions Endpoint { get; init; } = null!;

    /// <summary>Projects to target. Empty = all projects in the organisation.</summary>
    public List<string> Projects { get; init; } = new();

    /// <summary>
    /// Optional module-level scopes carried from the originating <c>OrganisationEntry</c>.
    /// Supported scope types: <c>wiql</c> (base query override) and <c>filter</c> (regex field filter).
    /// <see cref="Services.InventoryService"/> reads these when building the
    /// <see cref="WorkItemFetchScope"/> passed to <see cref="Services.IWorkItemDiscoveryService"/>.
    /// </summary>
    public IReadOnlyList<JobModuleScope> Scopes { get; init; } = System.Array.Empty<JobModuleScope>();
}
