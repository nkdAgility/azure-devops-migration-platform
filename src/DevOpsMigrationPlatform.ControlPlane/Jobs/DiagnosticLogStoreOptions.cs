// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Configuration for the <see cref="DiagnosticLogStore"/> ring buffer.
/// Bound from the <c>DiagnosticLog</c> configuration section.
/// </summary>
public sealed class DiagnosticLogStoreOptions
{
    public const string SectionName = "DiagnosticLog";

    /// <summary>Maximum number of records retained per job in the ring buffer.</summary>
    [Range(1, 100_000)]
    public int Capacity { get; init; } = 1000;

    /// <summary>
    /// Deployment-level minimum log level for the control plane.
    /// Records below this level are discarded before buffering.
    /// Default: <c>"Information"</c>. Override via configuration to restrict further.
    /// </summary>
    public string MinimumLevel { get; init; } = "Information";
}
