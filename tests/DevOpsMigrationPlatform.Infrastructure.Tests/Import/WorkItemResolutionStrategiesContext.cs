using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared state for Work Item Resolution Strategies step definitions.
/// </summary>
public class WorkItemResolutionStrategiesContext
{
    public Mock<IArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Strict);
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);

    public WorkItemsModuleExtensions Extensions { get; set; } = new WorkItemsModuleExtensions();

    public List<string> FolderPaths { get; set; } = new();

    /// <summary>Seed entries passed to SeedAsync.</summary>
    public List<IdMapEntry> SeededEntries { get; } = new();

    /// <summary>True when SeedAsync was called.</summary>
    public bool SeedAsyncCalled { get; set; }

    /// <summary>True when ResolveSingleAsync was called during processing.</summary>
    public bool ResolveSingleCalled { get; set; }

    /// <summary>Provenance entries written after creation.</summary>
    public List<(int SourceId, int TargetId)> ProvenanceEntries { get; } = new();

    public WorkItemImportOrchestrator BuildOrchestrator()
    {
        var mockIdentity = new Mock<IIdentityMappingService>(MockBehavior.Loose);
        var processor = new RevisionFolderProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            mockIdentity.Object,
            MockArtefactStore.Object,
            NullLogger<RevisionFolderProcessor>.Instance);

        return new WorkItemImportOrchestrator(
            MockArtefactStore.Object,
            MockCheckpointing.Object,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            NullLogger<WorkItemImportOrchestrator>.Instance);
    }
}
