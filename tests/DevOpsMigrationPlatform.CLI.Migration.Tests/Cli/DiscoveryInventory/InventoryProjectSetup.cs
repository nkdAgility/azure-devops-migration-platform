// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryInventory;

/// <summary>
/// Holds per-project event stream configuration for the mock IInventoryService.
/// </summary>
internal sealed class InventoryProjectSetup
{
    public string ProjectName { get; init; } = string.Empty;
    public int WorkItemsCount { get; init; }
    public int RevisionsCount { get; init; }
    public int ReposCount { get; init; }
    public int PipelinesCount { get; init; }
    public DateTime LastUpdatedUtc { get; init; }
}
