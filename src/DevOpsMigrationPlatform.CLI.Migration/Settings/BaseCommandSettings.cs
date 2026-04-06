using System.ComponentModel;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Base command settings providing common options for all CLI commands.
/// </summary>
public class BaseCommandSettings : CommandSettings, IHasConfigFile
{
    [CommandOption("-c|--config")]
    [Description("Path to the configuration file")]
    public string? ConfigFile { get; set; }

    [CommandOption("-v|--verbose")]
    [Description("Enable verbose output")]
    public bool Verbose { get; set; }

    [CommandOption("--disable-telemetry")]
    [Description("Disable telemetry collection")]
    public bool DisableTelemetry { get; set; }

    [CommandOption("--dry-run")]
    [Description("Perform a dry run without making changes")]
    public bool DryRun { get; set; }
}

/// <summary>
/// Interface for settings that support telemetry options.
/// </summary>
public interface IHasTelemetrySettings
{
    /// <summary>
    /// Whether telemetry collection is disabled.
    /// </summary>
    bool DisableTelemetry { get; }
}