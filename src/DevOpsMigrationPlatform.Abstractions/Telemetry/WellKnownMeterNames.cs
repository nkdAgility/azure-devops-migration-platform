// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Meter name constants shared across the solution.
/// Defined in Abstractions so .NET 10 hosts can register meters without referencing
/// the .NET 4.8 Infrastructure.TfsObjectModel assembly (Principle VI).
/// </summary>
public static class WellKnownMeterNames
{
    /// <summary>Unified meter for all agent metric instruments (inventory, analysis, export, import, validate, prepare, config).</summary>
    public const string Agent = "DevOpsMigrationPlatform.Agent";

    /// <summary>Meter for job lifecycle metrics (queue depth, in-progress, total runs).</summary>
    public const string ControlPlane = "DevOpsMigrationPlatform.ControlPlane";

    /// <summary>Meter for CLI command execution metrics (invocations, duration, errors).</summary>
    public const string Cli = "DevOpsMigrationPlatform.CLI";
}
