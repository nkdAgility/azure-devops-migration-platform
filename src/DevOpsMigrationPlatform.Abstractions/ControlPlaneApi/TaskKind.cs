// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Identifies the type of work a <see cref="JobTask"/> performs.
/// The executor dispatches on <see cref="TaskKind"/> — not on <see cref="JobTask.Phase"/>.
/// <see cref="JobTask.Phase"/> is a display hint only.
/// </summary>
public enum TaskKind
{
    /// <summary>
    /// Captures inventory data from the source for a single (org, project) scope.
    /// Dispatched via <c>ICapture.CaptureAsync</c> using the unified <c>captureHandlersByName</c> dictionary.
    /// Unrelated to export.
    /// </summary>
    Capture,

    /// <summary>
    /// Exports migration artefacts from the source into the package for a single (org, project) scope.
    /// Calls <c>IModule.ExportAsync</c>.
    /// </summary>
    Export,

    /// <summary>
    /// Imports migration artefacts from the package into the target for a single (org, project) scope.
    /// Calls <c>IModule.ImportAsync</c>.
    /// </summary>
    Import,

    /// <summary>
    /// Prepares the package against target preconditions.
    /// Calls <c>IModule.PrepareAsync</c>.
    /// </summary>
    Prepare,

    /// <summary>
    /// Fan-in analysis task. Reads partitioned outputs produced by <see cref="Capture"/> tasks,
    /// merges and normalises them, and writes the consolidated report.
    /// Calls <c>IInventoryAnalyser.AnalyseAsync</c> (or equivalent).
    /// Not a phase — runs after all capture tasks complete.
    /// </summary>
    Analyse,

    /// <summary>
    /// Validates the package or target without side effects.
    /// Calls <c>IModule.ValidateAsync</c>.
    /// </summary>
    Validate,

    /// <summary>
    /// Analyses cross-project or cross-org dependencies.
    /// </summary>
    Dependencies,
}
