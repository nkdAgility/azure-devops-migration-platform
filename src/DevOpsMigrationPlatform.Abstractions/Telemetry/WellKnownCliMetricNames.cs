// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// OpenTelemetry instrument name constants for CLI command metrics using the <c>cli.</c> prefix.
/// These names are the public contract — renaming is a breaking change requiring a version increment.
/// </summary>
public static class WellKnownCliMetricNames
{
    /// <summary>Total CLI command invocations (counter).</summary>
    public const string CommandInvocations = "cli.command.invocations";

    /// <summary>CLI command duration in milliseconds (histogram).</summary>
    public const string CommandDurationMs = "cli.command.duration_ms";

    /// <summary>CLI command errors (counter).</summary>
    public const string CommandErrors = "cli.command.errors";
}
