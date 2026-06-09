// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// Settings for the <c>discovery dependencies</c> command.
/// Reads the migration config via <c>--config</c> and writes a CSV report to <c>--output</c>.
/// </summary>
public sealed class DependencyCommandSettings : BaseCommandSettings, IRequiresMigrationConfig
{
    /// <summary>
    /// Path to the output CSV file.
    /// Defaults to <c>discovery-dependencies.csv</c> in the current working directory.
    /// </summary>
    [CommandOption("-o|--output")]
    [Description("Path to the output CSV file. Defaults to discovery-dependencies.csv in the current working directory.")]
    public string? OutputPath { get; init; }
}
