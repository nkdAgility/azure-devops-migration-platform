// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Captures the observable outcome of an in-process follow-stream scenario (B3).
/// Used exclusively by <see cref="ExportDiagnosticsScenario.RunWithActiveFollowStream"/>.
/// </summary>
public sealed class InProcessFollowResult
{
    /// <summary>
    /// True if the follow-stream CancellationToken was cancelled before
    /// the command method returned.
    /// </summary>
    public bool StreamCancelled { get; init; }

    /// <summary>
    /// True if the job-cancel endpoint was called on the mock IJobClient.
    /// Must be false for detach-without-cancel semantics.
    /// </summary>
    public bool JobCancelEndpointCalled { get; init; }

    /// <summary>
    /// Text written to the in-process console after stream cancellation.
    /// Checked for TUI-resume hint.
    /// </summary>
    public string ConsoleOutput { get; init; } = string.Empty;
}
