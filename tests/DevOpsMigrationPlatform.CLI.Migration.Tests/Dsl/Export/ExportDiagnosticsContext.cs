// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Context wrapper returned by every <see cref="ExportDiagnosticsScenario"/> entry point.
/// Wraps either a subprocess result (<see cref="SubprocessResult"/>) or an in-process
/// result (<see cref="InProcessResult"/>) depending on the scenario variant.
/// </summary>
public sealed class ExportDiagnosticsContext
{
    /// <summary>
    /// Subprocess result — populated for RunWithDefaultLevel, RunRemoteNoFollow,
    /// and RunStandaloneNoUrl.
    /// </summary>
    public CliRunner.TestCliResult? SubprocessResult { get; init; }

    /// <summary>
    /// In-process result — populated for RunWithActiveFollowStream.
    /// </summary>
    public InProcessFollowResult? InProcessResult { get; init; }

    /// <summary>
    /// Wall-clock elapsed time recorded during the run.
    /// Used by exit-immediacy assertions.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Resolved output directory. Only valid when <see cref="SubprocessResult"/> is set.
    /// </summary>
    public string? OutputDirectory => SubprocessResult?.OutputDirectory;
}
