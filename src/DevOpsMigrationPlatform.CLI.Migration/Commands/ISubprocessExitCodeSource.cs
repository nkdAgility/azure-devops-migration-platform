// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Provides the exit code produced by a subprocess (e.g. the TFS migration agent)
/// so that the CLI can propagate it as its own exit code.
///
/// In production, this is not registered in DI — the CLI reads the subprocess exit code
/// directly from <see cref="ChildProcessHost.Exited"/> after the job completes.
///
/// In tests, a fake implementation is registered so that
/// <see cref="QueueCommand"/> propagates the simulated subprocess exit code
/// without needing to launch real subprocesses.
/// </summary>
public interface ISubprocessExitCodeSource
{
    /// <summary>
    /// Returns the exit code that the subprocess produced.
    /// </summary>
    int GetExitCode();
}
