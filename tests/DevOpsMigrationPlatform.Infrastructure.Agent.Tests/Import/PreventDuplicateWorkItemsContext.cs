// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state for Prevent Duplicate Work Items step definitions.
/// Builds a real <see cref="WorkItemResolutionProcessor"/> with mocked dependencies.
/// </summary>
public class PreventDuplicateWorkItemsContext
{
    public Mock<ICheckpointingService> MockCheckpointing { get; } = new(MockBehavior.Strict);
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemImportTarget> MockTarget { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemResolutionStrategy> MockResolutionStrategy { get; } = new(MockBehavior.Strict);
    internal Mock<ITestArtefactStore> MockArtefactStore { get; } = new(MockBehavior.Loose);
    public Mock<IPackageAccess> MockPackage { get; }

    /// <summary>The folder path for the revision being processed.</summary>
    public string? FolderPath { get; set; }

    /// <summary>Tracks whether CreateWorkItemAsync was called.</summary>
    public bool CreateWorkItemCalled { get; set; }

    /// <summary>Tracks whether RecordSkippedRevisionAsync was called.</summary>
    public bool SkippedRevisionRecorded { get; set; }

    /// <summary>Reason recorded when a revision is skipped.</summary>
    public string? SkippedReason { get; set; }

    /// <summary>Source-to-target mapping recorded via SetWorkItemMappingAsync.</summary>
    public (int SourceId, int TargetId)? RecordedMapping { get; set; }

    /// <summary>Cursors written during processing.</summary>
    public List<CursorEntry> WrittenCursors { get; } = new();

    public PreventDuplicateWorkItemsContext()
    {
        MockPackage = PackageTestFactory.CreateDelegatingMock(MockArtefactStore.Object);
    }

    public WorkItemResolutionProcessor BuildProcessor()
    {
        return new WorkItemResolutionProcessor(
            MockTarget.Object,
            MockIdMapStore.Object,
            MockCheckpointing.Object,
            (IIdentityLookupTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            package: MockPackage.Object);
    }

    /// <summary>
    /// Sets up common mock registrations shared across all scenarios:
    /// checkpointing writes, comment.json returning null, and resolution strategy.
    /// </summary>
    public void SetupCommonMocks()
    {
        MockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, CursorEntry, CancellationToken>((_, cursor, _) => WrittenCursors.Add(cursor))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
    }
}
