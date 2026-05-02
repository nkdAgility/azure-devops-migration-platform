// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Shared scenario state for resumable batching cursor acceptance tests.
/// Injected into step definitions via Reqnroll's context injection.
/// </summary>
public sealed class ResumableBatchingCursorContext
{
    /// <summary>Saved continuation token from a prior run (or null if none exists).</summary>
    public BatchContinuationToken? SavedToken { get; set; }

    /// <summary>The resume decision returned by the strategy or fetch service.</summary>
    public ResumeDecision? Decision { get; set; }

    /// <summary>Exception thrown during resume attempt (if any).</summary>
    public Exception? ThrownException { get; set; }

    /// <summary>Work items yielded during the fetch enumeration.</summary>
    public List<FetchedWorkItem> YieldedItems { get; } = new();

    /// <summary>Continuation tokens emitted via the checkpoint writer callback.</summary>
    public List<BatchContinuationToken> EmittedCheckpoints { get; } = new();

    /// <summary>The query fingerprint for the current run.</summary>
    public string? CurrentFingerprint { get; set; }

    /// <summary>Whether resume was enabled for this test scenario.</summary>
    public bool ResumeEnabled { get; set; }

    /// <summary>Module name used for scoping continuation file paths.</summary>
    public string ModuleName { get; set; } = "test-module";
}
