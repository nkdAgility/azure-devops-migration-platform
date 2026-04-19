using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
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
