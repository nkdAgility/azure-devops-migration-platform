// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Settings for the <c>config new</c> sub-command (interactive configuration wizard).
/// </summary>
public sealed class ConfigNewCommandSettings : BaseCommandSettings
{
    [CommandOption("-o|--output")]
    [Description("Output file for the configuration (default: migration.json)")]
    public string? OutputFile { get; set; }

    [CommandOption("-f|--force")]
    [Description("Overwrite existing configuration file without prompting")]
    public bool Force { get; set; }
}

/// <summary>
/// Settings for the <c>config set &lt;key&gt; &lt;value&gt;</c> sub-command.
/// </summary>
public sealed class ConfigSetCommandSettings : CommandSettings
{
    [CommandArgument(0, "<key>")]
    [Description("The preference key to set (e.g. scenario-folder)")]
    public string Key { get; set; } = string.Empty;

    [CommandArgument(1, "<value>")]
    [Description("The value to assign to the preference key")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Settings for the <c>config get &lt;key&gt;</c> sub-command.
/// </summary>
public sealed class ConfigGetCommandSettings : CommandSettings
{
    [CommandArgument(0, "<key>")]
    [Description("The preference key to read (e.g. scenario-folder)")]
    public string Key { get; set; } = string.Empty;
}
