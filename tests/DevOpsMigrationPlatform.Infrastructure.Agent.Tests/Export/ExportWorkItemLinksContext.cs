// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

/// <summary>
/// Shared state for ExportWorkItemLinks step definitions.
/// </summary>
public class ExportWorkItemLinksContext
{
    public Mock<ICheckpointingService> MockCheckpointingService { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemRevisionSource> MockRevisionSource { get; } = new(MockBehavior.Strict);
    public FileSystemArtefactStore? RealArtefactStore { get; set; }
    public string? PackageRoot { get; set; }
    public WorkItemExportOrchestrator? Sut { get; set; }

    /// <summary>Revisions that the mock source yields.</summary>
    public List<WorkItemRevision> SourceRevisions { get; set; } = new();

    /// <summary>Captured exception from a run that is expected to fail.</summary>
    public Exception? ThrownException { get; set; }

    /// <summary>True once <c>SetupCursorNoOp</c> has been called.</summary>
    public bool IsCursorSetUp { get; set; }

    /// <summary>True once the revision source mock has been configured.</summary>
    public bool IsSourceSetUp { get; set; }
}
