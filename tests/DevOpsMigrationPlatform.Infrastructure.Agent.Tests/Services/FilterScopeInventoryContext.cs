// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;

/// <summary>
/// Shared state for FilterScopeInventory step definitions.
/// </summary>
public class FilterScopeInventoryContext
{
    public Mock<IWorkItemDiscoveryService> MockDiscovery { get; } = new(MockBehavior.Loose);
    public List<WorkItemFieldFilterOptions> FilterOptions { get; set; } = new();
    public string? CustomWiqlQuery { get; set; }
    public int DiscoveryCallCount { get; set; }
    public WorkItemFetchScope? LastFetchScope { get; set; }
    public int InventoryResult { get; set; }
}
