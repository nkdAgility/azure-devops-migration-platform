using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

/// <summary>
/// Shared state for FilterScopeExport step definitions.
/// </summary>
public class FilterScopeExportContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Loose);
    public Mock<ICheckpointingService> MockCheckpointingService { get; } = new(MockBehavior.Loose);
    public Mock<IWorkItemRevisionSource> MockRevisionSource { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemFetchService> MockFetchService { get; } = new(MockBehavior.Loose);

    public List<WorkItemRevision> SourceRevisions { get; set; } = new();
    public List<WorkItemFieldFilterOptions> FilterOptions { get; set; } = new();
    public List<string> WrittenPaths { get; } = new();
    public List<string> LogWarnings { get; } = new();

    public WorkItemExportOrchestrator? Sut { get; set; }
}
