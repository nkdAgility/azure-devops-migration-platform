// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.CLI.Migration;

/// <summary>
/// Resolves the initial UI metrics snapshot from control-plane responses.
/// Bootstrap is the authoritative first-render source and must win over
/// concurrent telemetry polling when both are available.
/// </summary>
internal static class BootstrapUiStateResolver
{
    /// <summary>
    /// Returns the preferred metrics snapshot for initial UI hydration.
    /// Bootstrap metrics take precedence when present; telemetry is only a fallback.
    /// </summary>
    public static JobMetrics? ResolvePreferredMetrics(JobBootstrap? bootstrap, JobMetrics? telemetry)
        => bootstrap?.Metrics ?? telemetry;
}