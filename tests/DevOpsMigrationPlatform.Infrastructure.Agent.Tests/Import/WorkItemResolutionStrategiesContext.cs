// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared state for Work Item Resolution Strategies step definitions.
/// </summary>
public class WorkItemResolutionStrategiesContext
{
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IProgressSink> MockProgressSink { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<IPackageAccess> MockPackage { get; }

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

    public WorkItemResolutionStrategiesContext()
    {
        MockPackage = PackageTestFactory.CreateLooseMock();
    }

    public WorkItemRevisionLoopDriver BuildOrchestrator()
    {
        var processor = new WorkItemResolutionProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            (IIdentityTranslationTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All) },
            package: MockPackage.Object);

        return new WorkItemRevisionLoopDriver(new WorkItemRevisionJobScope(
            MockPackage.Object,
            "https://dev.azure.com/contoso",
            "Shop",
            MockCheckpointing.Object,
            MockProgressSink.Object,
            MockResolutionStrategy.Object,
            MockIdMapStore.Object,
            processor,
            MockTarget.Object,
            JobId: null,
            FilterOptions: null));
    }
}
