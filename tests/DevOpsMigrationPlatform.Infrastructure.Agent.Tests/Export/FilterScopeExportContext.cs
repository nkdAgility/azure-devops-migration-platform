// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using Moq;
namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;
/// <summary>
/// Shared state for FilterScopeExport step definitions.
/// </summary>
public class FilterScopeExportContext
{
    public Mock<IPackageAccess> MockPackage { get; } = new(MockBehavior.Loose);
    public Mock<ICheckpointingService> MockCheckpointingService { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemRevisionSource> MockRevisionSource { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemFetchService> MockFetchService { get; } = new(MockBehavior.Loose);
    public List<WorkItemRevision> SourceRevisions { get; set; } = new();
    public List<WorkItemFieldFilterOptions> FilterOptions { get; set; } = new();
    public List<string> WrittenPaths { get; } = new();
    public List<string> LogWarnings { get; } = new();
    public WorkItemExportOrchestrator? Sut { get; set; }
}
