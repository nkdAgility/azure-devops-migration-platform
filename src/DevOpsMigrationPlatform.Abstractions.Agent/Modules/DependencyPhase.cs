// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Specifies the job phase(s) during which a module dependency applies.
/// </summary>
public enum DependencyPhase
{
    /// <summary>
    /// Dependency applies only during the Export phase.
    /// The dependent module's export task will wait for the dependency's export task to complete.
    /// </summary>
    Export = 1,

    /// <summary>
    /// Dependency applies only during the Import phase.
    /// The dependent module's import task will wait for the dependency's import task to complete.
    /// </summary>
    Import = 2,

    /// <summary>
    /// Dependency applies during both Export and Import phases.
    /// Both export and import tasks will wait for the corresponding dependency tasks to complete.
    /// </summary>
    Both = 3
}
