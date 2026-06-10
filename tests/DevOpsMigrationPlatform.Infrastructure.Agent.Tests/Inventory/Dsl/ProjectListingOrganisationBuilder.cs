// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Fluent builder for <see cref="OrganisationEndpoint"/> instances used in project-listing tests.
/// Follows the <see cref="OrganisationEntryBuilder"/> pattern — no live credentials required.
/// </summary>
internal static class ProjectListingOrganisationBuilder
{
    /// <summary>
    /// Returns a minimal <see cref="OrganisationEndpoint"/> with a reachable URL and a valid PAT.
    /// Suitable for tests that stub <c>IProjectDiscoveryService</c>.
    /// </summary>
    public static OrganisationEndpoint Reachable(
        string url = "https://dev.azure.com/testorg",
        string pat = "test-pat") =>
        new()
        {
            ResolvedUrl = url,
            Type = "AzureDevOps",
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = pat
            }
        };
}
