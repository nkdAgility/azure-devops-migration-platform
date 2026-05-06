// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

/// <summary>
/// Extends <see cref="IAnalyser"/> with per-project capture support.
/// Implementations perform a single-project discovery pass and write
/// per-project artefacts that a subsequent <see cref="IAnalyser.AnalyseAsync"/> fan-in step consolidates.
/// </summary>
public interface IProjectAnalyser : IAnalyser
{
    /// <summary>
    /// Captures analysis data for a single org+project pair.
    /// Called by the plan executor for each <c>capture.{name}.{org}.{project}</c> task.
    /// Results are written to a per-project artefact path so the fan-in step can consolidate them.
    /// </summary>
    Task CaptureProjectAsync(InventoryContext context, CancellationToken ct);
}
