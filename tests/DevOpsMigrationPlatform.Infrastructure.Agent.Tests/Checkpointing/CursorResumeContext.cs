// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Shared scenario state and mocks for CursorResume step definitions.
/// </summary>
public class CursorResumeContext
{
    /// <summary>Strict mock for the state store — modules must not call this directly.</summary>
    public Mock<IStateStore> MockStateStore { get; } = new Mock<IStateStore>(MockBehavior.Strict);

    /// <summary>Strict mock used for scenarios that verify module-level delegation of cursor writes.</summary>
    public Mock<ICheckpointingService> MockCheckpointingService { get; } = new Mock<ICheckpointingService>(MockBehavior.Strict);

    /// <summary>The concrete CheckpointingService under test, backed by MockStateStore.</summary>
    public CheckpointingService Sut { get; }

    // ── scenario state ──────────────────────────────────────────────

    /// <summary>The cursor key used in the current scenario, e.g. "Checkpoints/workitems.cursor.json".</summary>
    public string? CursorKey { get; set; }

    /// <summary>The cursor entry written during the current scenario.</summary>
    public CursorEntry? WrittenCursorEntry { get; set; }

    /// <summary>Ordered list of folder paths fed into the module during the scenario.</summary>
    public List<string> AllFolders { get; set; } = new();

    /// <summary>Folder paths that were actually handed to the processing callback (i.e. not skipped).</summary>
    public List<string> ProcessedPaths { get; } = new();

    /// <summary>Number of folders successfully processed before a simulated crash.</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Set to true when a crash has been simulated in the scenario.</summary>
    public bool CrashSimulated { get; set; }

    /// <summary>The raw cursor value present in the state store at the start of the scenario.</summary>
    public string? InitialCursorValue { get; set; }

    /// <summary>Cursor entries keyed by module name, captured as modules write them.</summary>
    public Dictionary<string, CursorEntry?> CursorsByModule { get; } = new();

    public CursorResumeContext()
    {
        Sut = new CheckpointingService(MockStateStore.Object);
    }
}
