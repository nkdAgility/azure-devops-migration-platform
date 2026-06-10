// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Platform-level constant values used by inventory scope assertions.
/// </summary>
internal static class PlatformDefaults
{
    /// <summary>
    /// The default WIQL query used when no wiql scope is configured for an organisation.
    /// Represented as null/absent BaseQuery in WorkItemFetchScope — this constant is kept
    /// here for documentation and assertion reference only.
    /// </summary>
    public const string WorkItemQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";
}
