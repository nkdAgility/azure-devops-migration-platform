// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Configuration for the <see cref="DiagnosticLogStore"/> append-only log.
/// Bound from the <c>DiagnosticLog</c> configuration section.
/// </summary>
public sealed class DiagnosticLogStoreOptions
{
    public const string SectionName = "DiagnosticLog";

    /// <summary>
    /// Deployment-level minimum log level for the control plane.
    /// Records below this level are discarded before buffering.
    /// Default: <c>"Information"</c>. Override via configuration to restrict further.
    /// </summary>
    public string MinimumLevel { get; init; } = "Information";

    /// <summary>
    /// Maximum records retained per job before further records are discarded with a warning.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxRecordsPerJob { get; init; } = 50_000;
}
